using System.Linq;
using System.Numerics;
using Content.Client.Stylesheets;
using Content.Client.UserInterface.Controls;
using Content.Shared.Medical.SuitSensor;
using Robust.Client.AutoGenerated;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.XAML;
using Robust.Shared.Map;
using Robust.Shared.Timing;
using static Robust.Client.UserInterface.Controls.BoxContainer;

namespace Content.Client.Medical.CrewMonitoring
{
    [GenerateTypedNameReferences]
    public sealed partial class CrewMonitoringWindow : FancyWindow
    {
        private List<Control> _rowsContent = new();
        private List<(DirectionIcon Icon, Vector2 Position)> _directionIcons = new();
        private readonly IEntityManager _entManager;
        private readonly IEyeManager _eye;
        private EntityUid? _stationUid;
        private CrewMonitoringButton? _trackedButton;

        public static int IconSize = 16; // XAML has a `VSeparationOverride` of 20 for each row.

        public CrewMonitoringWindow(EntityUid? mapUid)
        {
            RobustXamlLoader.Load(this);
            _eye = IoCManager.Resolve<IEyeManager>();
            _entManager = IoCManager.Resolve<IEntityManager>();
            _stationUid = mapUid;

            if (_entManager.TryGetComponent<TransformComponent>(mapUid, out var xform))
            {
                NavMap.MapUid = mapUid;
            }
            else
            {
                NavMap.Visible = false;
                SetSize = new Vector2(775, 400);
                MinSize = SetSize;
            }
        }

        public void ShowSensors(List<SuitSensorStatus> stSensors, EntityCoordinates? monitorCoords, bool snap, float precision)
        {
            ClearAllSensors();

            var monitorCoordsInStationSpace = _stationUid != null ? monitorCoords?.WithEntityId(_stationUid.Value, _entManager).Position : null;

            // TODO scroll container
            // TODO filter by name & occupation
            // TODO make each row a xaml-control. Get rid of some of this c# control creation.
            if (stSensors.Count == 0)
            {
                NoServerLabel.Visible = true;
                return;
            }
            NoServerLabel.Visible = false;

            // add a row for each sensor
            foreach (var sensor in stSensors.OrderBy(a => a.Name))
            {
                var sensorEntity = _entManager.GetEntity(sensor.SuitSensorUid);
                var coordinates = _entManager.GetCoordinates(sensor.Coordinates);

                // add button with username
                var nameButton = new CrewMonitoringButton()
                {
                    SuitSensorUid = sensorEntity,
                    Coordinates = coordinates,
                    Text = sensor.Name,
                    Margin = new Thickness(5f, 5f),
                };
                if (sensorEntity == _trackedButton?.SuitSensorUid)
                    nameButton.AddStyleClass(StyleNano.StyleClassButtonColorGreen);
                SetColorLabel(nameButton.Label, sensor.TotalDamage, sensor.IsAlive);
                SensorsTable.AddChild(nameButton);
                _rowsContent.Add(nameButton);

                // add users job
                // format: JobName
                var jobLabel = new Label()
                {
                    Text = sensor.Job,
                    HorizontalExpand = true
                };
                SetColorLabel(jobLabel, sensor.TotalDamage, sensor.IsAlive);
                SensorsTable.AddChild(jobLabel);
                _rowsContent.Add(jobLabel);

                // add users status and damage
                // format: IsAlive (TotalDamage)
                var statusText = Loc.GetString(sensor.IsAlive ?
                    "crew-monitoring-user-interface-alive" :
                    "crew-monitoring-user-interface-dead");
                if (sensor.TotalDamage != null)
                {
                    statusText += $" ({sensor.TotalDamage})";
                }
                var statusLabel = new Label()
                {
                    Text = statusText
                };
                SetColorLabel(statusLabel, sensor.TotalDamage, sensor.IsAlive);
                SensorsTable.AddChild(statusLabel);
                _rowsContent.Add(statusLabel);

                // add users positions
                // format: (x, y)
                var box = GetPositionBox(sensor, monitorCoordsInStationSpace ?? Vector2.Zero, snap, precision);

                SensorsTable.AddChild(box);
                _rowsContent.Add(box);

                if (coordinates != null && NavMap.Visible)
                {
                    NavMap.TrackedCoordinates.TryAdd(coordinates.Value,
                        (true, sensorEntity == _trackedButton?.SuitSensorUid ? StyleNano.PointGreen : StyleNano.PointRed));

                    nameButton.OnButtonUp += args =>
                    {
                        if (_trackedButton != null && _trackedButton?.Coordinates != null)
                            //Make previous point red
                            NavMap.TrackedCoordinates[_trackedButton.Coordinates.Value] = (true, StyleNano.PointRed);

                        NavMap.TrackedCoordinates[coordinates.Value] = (true, StyleNano.PointGreen);
                        NavMap.CenterToCoordinates(coordinates.Value);

                        nameButton.AddStyleClass(StyleNano.StyleClassButtonColorGreen);
                        if (_trackedButton != null)
                        {   //Make previous button default
                            var previosButton = SensorsTable.GetChild(_trackedButton.IndexInTable);
                            previosButton.RemoveStyleClass(StyleNano.StyleClassButtonColorGreen);
                        }
                        _trackedButton = nameButton;
                        _trackedButton.IndexInTable = nameButton.GetPositionInParent();
                    };
                }
            }
            // Show monitor point
            if (monitorCoords != null)
                NavMap.TrackedCoordinates.Add(monitorCoords.Value, (true, StyleNano.PointMagenta));
        }

        private BoxContainer GetPositionBox(SuitSensorStatus sensor, Vector2 monitorCoordsInStationSpace, bool snap, float precision)
        {
            EntityCoordinates? coordinates = _entManager.GetCoordinates(sensor.Coordinates);
            var box = new BoxContainer() { Orientation = LayoutOrientation.Horizontal };

            if (coordinates == null || _stationUid == null)
            {
                var dirIcon = new DirectionIcon()
                {
                    SetSize = new Vector2(IconSize, IconSize),
                    Margin = new(0, 0, 4, 0)
                };
                box.AddChild(dirIcon);
                box.AddChild(new Label() { Text = Loc.GetString("crew-monitoring-user-interface-no-info") });
            }
            else
            {
                var local = coordinates.Value.WithEntityId(_stationUid.Value, _entManager).Position;

                var displayPos = local.Floored();
                var dirIcon = new DirectionIcon(snap, precision)
                {
                    SetSize = new Vector2(IconSize, IconSize),
                    Margin = new(0, 0, 4, 0)
                };
                box.AddChild(dirIcon);
                Label label = new Label() { Text = displayPos.ToString() };
                SetColorLabel(label, sensor.TotalDamage, sensor.IsAlive);
                box.AddChild(label);
                _directionIcons.Add((dirIcon, local - monitorCoordsInStationSpace));
            }

            return box;
        }

        protected override void FrameUpdate(FrameEventArgs args)
        {
            // the window is separate from any specific viewport, so there is no real way to get an eye-rotation without
            // using IEyeManager. Eventually this will have to be reworked for a station AI with multi-viewports.
            // (From the future: Or alternatively, just disable the angular offset for station AIs?)

            // An offsetAngle of zero here perfectly aligns directions to the station map.
            // Note that the "relative angle" does this weird inverse-inverse thing.
            // Could recalculate it all in world coordinates and then pass in eye directly... or do this.
            var offsetAngle = Angle.Zero;
            if (_entManager.TryGetComponent<TransformComponent>(_stationUid, out var xform))
            {
                // Apply the offset relative to the eye.
                // For a station at 45 degrees rotation, the current eye rotation is -45 degrees.
                // TODO: This feels sketchy. Is there something underlying wrong with eye rotation?
                offsetAngle = -(_eye.CurrentEye.Rotation + xform.WorldRotation);
            }

            foreach (var (icon, pos) in _directionIcons)
            {
                icon.UpdateDirection(pos, offsetAngle);
            }
        }

        private void ClearAllSensors()
        {
            foreach (var child in _rowsContent)
            {
                SensorsTable.RemoveChild(child);
            }
            _rowsContent.Clear();
            _directionIcons.Clear();
            NavMap.TrackedCoordinates.Clear();
        }

        private void SetColorLabel(Label label, int? totalDamage, bool isAlive)
        {
            var startColor = Color.White;
            var critColor = Color.Yellow;
            var endColor = Color.Red;

            if (!isAlive)
            {
                label.FontColorOverride = endColor;
                return;
            }

            //Convert from null to regular int
            int damage;
            if (totalDamage == null) return;
            else damage = (int) totalDamage;

            if (damage <= 0)
            {
                label.FontColorOverride = startColor;
            }
            else if (damage >= 200)
            {
                label.FontColorOverride = endColor;
            }
            else if (damage >= 0 && damage <= 100)
            {
                label.FontColorOverride = GetColorLerp(startColor, critColor, damage);
            }
            else if (damage >= 100 && damage <= 200)
            {
                //We need a number from 0 to 100. Divide the number from 100 to 200 by 2
                damage /= 2;
                label.FontColorOverride = GetColorLerp(critColor, endColor, damage);
            }
        }

        private Color GetColorLerp(Color startColor, Color endColor, int damage)
        {
            //Smooth transition from one color to another depending on the percentage
            var t = damage / 100f;
            var r = MathHelper.Lerp(startColor.R, endColor.R, t);
            var g = MathHelper.Lerp(startColor.G, endColor.G, t);
            var b = MathHelper.Lerp(startColor.B, endColor.B, t);
            var a = MathHelper.Lerp(startColor.A, endColor.A, t);

            return new Color(r, g, b, a);
        }
    }

    public sealed class CrewMonitoringButton : Button
    {
        public int IndexInTable;
        public EntityUid? SuitSensorUid;
        public EntityCoordinates? Coordinates;
    }
}
