using System.IO;
using Fieldmate.AI;
using UnityEngine;

namespace Fieldmate.Providers;

/// <summary>
/// Builds the assistant's providers from keys.json (ADR-003). Sources, first match wins:
/// 1. <c>persistentDataPath/keys.json</c> on the headset, pushed by <c>tools/push-keys.sh</c> (can carry the dev token);
/// 2. <c>Assets/_Project/Secrets/keys.json</c> in the Editor;
/// 3. the committed public default <c>Resources/FieldmateProxy.json</c> (proxy URL only).
/// When none yields a proxy URL, the assistant is disabled and <see cref="DisabledReason"/> says why.
/// </summary>
public sealed class AssistantProviders
{
    public const string KeysFileName = "keys.json";
    public const string PublicDefaultResource = "FieldmateProxy";
    public const string EditorKeysPath = "Assets/_Project/Secrets/keys.json";

    private AssistantProviders(ProxyConfig config, string source, IChatModel chat, ISpeechToText stt, IStreamingTextToSpeech tts)
    {
        Config = config;
        Source = source;
        Chat = chat;
        SpeechToText = stt;
        TextToSpeech = tts;
    }

    private AssistantProviders(string disabledReason) => DisabledReason = disabledReason;

    public bool IsEnabled => Config != null;
    public string DisabledReason { get; }
    public ProxyConfig Config { get; }

    /// <summary>Where the config came from (for the debug panel).</summary>
    public string Source { get; }

    public IChatModel Chat { get; }
    public ISpeechToText SpeechToText { get; }
    public IStreamingTextToSpeech TextToSpeech { get; }

    /// <summary>Loads config from the standard sources and creates UnityWebRequest-backed providers.</summary>
    public static AssistantProviders Load()
    {
        var candidates = new (string source, string json)[]
        {
            ("device keys.json", ReadFile(Path.Combine(Application.persistentDataPath, KeysFileName))),
#if UNITY_EDITOR
            ("editor keys.json", ReadFile(EditorKeysPath)),
#endif
            ("public default", Resources.Load<TextAsset>(PublicDefaultResource)?.text),
        };

        return FromCandidates(candidates, new UnityHttpTransport());
    }

    /// <summary>First candidate with a usable proxy URL wins; otherwise the last reason is reported.</summary>
    public static AssistantProviders FromCandidates((string source, string json)[] candidates, IHttpTransport transport)
    {
        var reason = "No proxy URL configured (keys.json \"proxyUrl\").";
        foreach (var (source, json) in candidates)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                continue;
            }

            if (ProxyConfig.TryParse(json, out var config, out var why))
            {
                var client = new ProxyClient(config, transport);
                return new AssistantProviders(config, source, new ProxyChatModel(client), new ProxySpeechToText(client),
                    new ProxyTextToSpeech(client));
            }

            reason = $"{source}: {why}";
        }

        return new AssistantProviders($"Assistant offline. {reason} Answers use the offline fallback.");
    }

    private static string ReadFile(string path) => File.Exists(path) ? File.ReadAllText(path) : null;
}
