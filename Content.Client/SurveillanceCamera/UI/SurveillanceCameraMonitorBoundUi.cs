using Content.Client.Eye;
using Content.Shared.SurveillanceCamera;
using Robust.Client.GameObjects;

namespace Content.Client.SurveillanceCamera.UI;

public sealed class SurveillanceCameraMonitorBoundUserInterface : BoundUserInterface
{
    [Dependency] private readonly IEntityManager _entityManager = default!;
    private EyeLerpingSystem _eyeLerpingSystem = default!;

    private SurveillanceCameraMonitorWindow? _window;
    private EntityUid? _currentCamera;
    private string _currentSubnet = default!;
    private readonly HashSet<string> _knownAddresses = new();
    private readonly Dictionary<string, SurveillanceCameraInfo> _knownCameras = new();

    public SurveillanceCameraMonitorBoundUserInterface(ClientUserInterfaceComponent owner, object uiKey) : base(owner, uiKey)
    {
        IoCManager.InjectDependencies(this);
        _eyeLerpingSystem = EntitySystem.Get<EyeLerpingSystem>();
    }

    protected override void Open()
    {
        base.Open();

        _window = new SurveillanceCameraMonitorWindow();

        if (State != null)
        {
            UpdateState(State);
        }

        _window.OpenCentered();

        _window.CameraSelected += OnCameraSelected;
        _window.SubnetOpened += OnSubnetRequest;
        _window.CameraRefresh += OnCameraRefresh;
        _window.SubnetRefresh += OnSubnetRefresh;
        _window.OnClose += Close;
    }

    private void OnCameraSelected(string address)
    {
        SendMessage(new SurveillanceCameraMonitorSwitchMessage(address));
    }

    private void OnSubnetRequest(string subnet)
    {
        SendMessage(new SurveillanceCameraMonitorSubnetRequestMessage(subnet));
    }

    private void OnCameraRefresh()
    {
        SendMessage(new SurveillanceCameraRefreshCamerasMessage());
    }

    private void OnSubnetRefresh()
    {
        SendMessage(new SurveillanceCameraRefreshSubnetsMessage());
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        if (_window == null || state is not SurveillanceCameraMonitorUiState cast)
        {
            return;
        }

        if (_currentSubnet != cast.ActiveSubnet)
        {
            _knownCameras.Clear();
            _currentSubnet = cast.ActiveSubnet;
        }

        foreach (var camera in cast.Cameras)
        {
            if (!_knownCameras.ContainsKey(camera.Address))
            {
                _knownCameras.Add(camera.Address, camera);
            }
        }

        if (cast.ActiveCamera == null)
        {
            _window.UpdateState(null, cast.Subnets, _currentSubnet, _knownCameras.Values);

            if (_currentCamera != null)
            {
                _eyeLerpingSystem.RemoveEye(_currentCamera.Value);
                _currentCamera = null;
            }
        }
        else
        {
            if (_currentCamera == null)
            {
                _eyeLerpingSystem.AddEye(cast.ActiveCamera.Value);
                _currentCamera = cast.ActiveCamera;
            }
            else if (_currentCamera != cast.ActiveCamera)
            {
                _eyeLerpingSystem.RemoveEye(_currentCamera.Value);
                _eyeLerpingSystem.AddEye(cast.ActiveCamera.Value);
                _currentCamera = cast.ActiveCamera;
            }

            if (_entityManager.TryGetComponent(cast.ActiveCamera, out EyeComponent eye))
            {
                _window.UpdateState(eye.Eye, cast.Subnets, _currentSubnet, _knownCameras.Values);
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            _window?.Dispose();
        }
    }
}
