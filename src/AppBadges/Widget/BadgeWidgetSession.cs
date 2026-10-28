using MacroDeck.Localization;
using System.Text.Json;
using AppBadges.Badges;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Model.Resources;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using MacroDeck.Ui.Widgets;
using Serilog;

namespace AppBadges.Widget;

/// <summary>One placed widget as one viewer sees it: follows the app's badge and opens the app on a tap.</summary>
internal sealed class BadgeWidgetSession : IUiSession
{
	private readonly InstalledApp? _app;
	private readonly string _bundleId;
	private readonly UiResource? _icon;
	private readonly string? _caption;
	private readonly BadgeMonitor _monitor;
	private readonly IAppLauncher _launcher;
	private readonly ILogger _logger;
	private readonly UiState<BadgeFace> _face;
	private readonly UiView _view;
	private readonly Lock _sync = new();

	private bool _openRequested;
	private bool _disposed;

	public BadgeWidgetSession(
		UiSurface surface,
		JsonElement data,
		InstalledApp? app,
		string bundleId,
		UiResource? icon,
		string? caption,
		BadgeMonitor monitor,
		IAppLauncher launcher,
		ILogger logger)
	{
		_app = app;
		_bundleId = bundleId;
		_icon = icon;
		_caption = caption;
		_monitor = monitor;
		_launcher = launcher;
		_logger = logger;

		_face = new UiState<BadgeFace>(BadgeFace.For(app, bundleId, icon, monitor.Current(bundleId), caption));
		_view = new UiView(surface, BadgeWidgetView.Build(_face, data, UiWidgetSafeArea.RadiusOf(surface),
			[UiEventHandler.On(UiComponentEvents.Press, () => _openRequested = true)]));
		_view.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
		_view.HandlerFaulted += (_, fault)
			=> Faulted?.Invoke(this, new UiSessionFaultedEventArgs(fault.Exception.Message, fault.Exception));

		if (_app is not null)
		{
			_monitor.Changed += OnBadgeChanged;
			_monitor.Watch(_bundleId);

			// A read that landed between building the first face and subscribing above is not lost.
			Refresh();
		}
	}

	public event EventHandler? Changed;

	public event EventHandler<UiSessionFaultedEventArgs>? Faulted;

	public UiTree BuildTree()
	{
		lock (_sync)
		{
			return _view.Tree;
		}
	}

	public IReadOnlyList<UiPatch> DrainPatches()
	{
		lock (_sync)
		{
			return _view.DrainPatches();
		}
	}

	public void Dispatch(UiEvent uiEvent)
	{
		DispatchFrom(uiEvent, null);
	}

	public void Dispatch(UiEvent uiEvent, IUiEventOrigin origin)
	{
		DispatchFrom(uiEvent, origin);
	}

	// The press handler only records the request; the app is opened here, outside the lock.
	private void DispatchFrom(UiEvent uiEvent, IUiEventOrigin? origin)
	{
		bool open;

		lock (_sync)
		{
			if (_disposed)
			{
				return;
			}

			_view.Dispatch(uiEvent);
			open = _openRequested;
			_openRequested = false;
		}

		if (!open)
		{
			return;
		}

		if (_app is { } app)
		{
			_ = OpenAsync(app, origin);
		}
		else
		{
			_ = ReportAsync(origin, Strings.Face.NotInstalled());
		}
	}

	public ValueTask DisposeAsync()
	{
		if (_app is not null)
		{
			_monitor.Changed -= OnBadgeChanged;
		}

		lock (_sync)
		{
			if (_disposed)
			{
				return ValueTask.CompletedTask;
			}

			_disposed = true;
			_view.Dispose();
		}

		if (_app is not null)
		{
			_monitor.Unwatch(_bundleId);
		}

		return ValueTask.CompletedTask;
	}

	private async Task OpenAsync(InstalledApp app, IUiEventOrigin? origin)
	{
		bool opened;

		try
		{
			opened = await _launcher.OpenAsync(app, CancellationToken.None).ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			_logger.Warning(exception, "Could not open {App}", app.Name);
			opened = false;
		}

		if (opened)
		{
			return;
		}

		_logger.Warning("Opening {App} ({Path}) failed", app.Name, app.Path);

		await ReportAsync(origin, Strings.Errors.OpenFailed(app.Name)).ConfigureAwait(false);
	}

	// A hardware deck has no screen for the message; the log line is what remains there.
	private async Task ReportAsync(IUiEventOrigin? origin, LocalizedText message)
	{
		if (origin is null)
		{
			return;
		}

		try
		{
			await origin.ReportFailureAsync(message).ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			// The client may have gone away since the tap; nobody is left to tell.
			_logger.Debug(exception, "Could not report a failure to the client");
		}
	}

	private void OnBadgeChanged(object? sender, BadgeChangedEventArgs args)
	{
		if (args.BundleId == _bundleId)
		{
			Refresh();
		}
	}

	private void Refresh()
	{
		lock (_sync)
		{
			if (_disposed)
			{
				return;
			}

			var face = BadgeFace.For(_app, _bundleId, _icon, _monitor.Current(_bundleId), _caption);

			if (face != _face.Value)
			{
				using (_view.Batch())
				{
					_face.Set(face);
				}
			}
		}
	}
}
