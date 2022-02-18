using Robust.Shared.Serialization;

namespace Content.Shared.CloningConsole
{
    public abstract class SharedCloningConsoleComponent : Component
    {
        [Serializable, NetSerializable]
        public sealed class CloningConsoleBoundUserInterfaceState : BoundUserInterfaceState
        {
            public readonly string? ScannerBodyInfo;
            public readonly string? ClonerBodyInfo;
            public readonly List<string> CloneHistory;
            // When this state was created.
            // The reason this is used rather than a start time is because cloning can be interrupted.
            public readonly TimeSpan ReferenceTime;
            // Both of these are in seconds.
            // They're not TimeSpans because of complicated reasons.
            // CurTime of receipt is combined with Progress.
            public readonly float Progress;
            public readonly float Maximum;
            // If true, cloning is progressing (predict clone progress)
            public readonly bool Progressing;
            public readonly bool MindPresent;
            public readonly ClonerStatusState CloningStatus;
            public readonly bool ScannerConnected;
            public readonly bool ClonerConnected;
            public CloningConsoleBoundUserInterfaceState(string? scannerBodyInfo, string? cloningBodyInfo, List<string> cloneHistory, TimeSpan refTime, float progress, float maximum, bool progressing, bool mindPresent, ClonerStatusState cloningStatus, bool scannerConnected, bool clonerConnected)
            {
                ScannerBodyInfo = scannerBodyInfo;
                ClonerBodyInfo = cloningBodyInfo;
                CloneHistory = cloneHistory;
                ReferenceTime = refTime;
                Progress = progress;
                Maximum = maximum;
                Progressing = progressing;
                MindPresent = mindPresent;
                CloningStatus = cloningStatus;
                ScannerConnected = scannerConnected;
                ClonerConnected = clonerConnected;
            }
        }

        [Serializable, NetSerializable]
        public enum ClonerStatusState
        {
            Ready,
            ScannerEmpty,
            ScannerOccupantAlive,
            OccupantMetaphyiscal,
            ClonerOccupied,
            NoClonerDetected,
            NoMindDetected
        }

        [Serializable, NetSerializable]
        public enum CloningConsoleUiKey
        {
            Key
        }

        [Serializable, NetSerializable]
        public enum UiButton
        {
            Clone,
            Eject

        }

        [Serializable, NetSerializable]
        public sealed class UiButtonPressedMessage : BoundUserInterfaceMessage
        {
            public readonly UiButton Button;

            public UiButtonPressedMessage(UiButton button)
            {
                Button = button;
            }
        }
    }
}
