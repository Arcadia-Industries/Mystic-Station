using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.Speech
{
    /// <summary>
    ///     Contains the option to let entities make noise when speaking, change speech verbs, datafields for the sounds in question, and relevant AudioParams.
    /// </summary>
    [RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
    public sealed partial class SpeechComponent : Component
    {
        [DataField, AutoNetworkedField]
        [Access(typeof(SpeechSystem), Friend = AccessPermissions.ReadWrite, Other = AccessPermissions.Read)]
        public bool Enabled = true;

        [ViewVariables(VVAccess.ReadWrite)]
        [DataField]
        public ProtoId<SpeechSoundsPrototype>? SpeechSounds;

        /// <summary>
        ///     What speech verb prototype should be used by default for displaying this entity's messages?
        /// </summary>
        [ViewVariables(VVAccess.ReadWrite)]
        [DataField]
        public ProtoId<SpeechVerbPrototype> SpeechVerb = "Default";

        /// <summary>
        ///     A mapping from chat suffixes loc strings to speech verb prototypes that should be conditionally used.
        ///     For things like '?' changing to 'asks' or '!!' making text bold and changing to 'yells'. Can be overridden if necessary.
        /// </summary>
        [DataField]
        public Dictionary<string, ProtoId<SpeechVerbPrototype>> SuffixSpeechVerbs = new()
        {
            { "chat-speech-verb-suffix-exclamation-strong", "DefaultExclamationStrong" },
            { "chat-speech-verb-suffix-exclamation", "DefaultExclamation" },
            { "chat-speech-verb-suffix-question", "DefaultQuestion" },
            { "chat-speech-verb-suffix-stutter", "DefaultStutter" },
            { "chat-speech-verb-suffix-mumble", "DefaultMumble" },
        };

        [DataField]
        public AudioParams AudioParams = AudioParams.Default.WithVolume(-2f).WithRolloffFactor(4.5f);

        [ViewVariables(VVAccess.ReadWrite)]
        [DataField]
        public float SoundCooldownTime { get; set; } = 0.5f;

        public TimeSpan LastTimeSoundPlayed = TimeSpan.Zero;
    }
}
