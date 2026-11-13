using Geminarr.Core.Configuration;
using Microsoft.Extensions.Options;

namespace Geminarr.Core.Pipeline;

/// <summary>
/// A demonstration reader with synthetic conversations that grow: for a sandbox and the tests, never for a real deployment (it is chosen with <c>GEMINARR__READER__KIND=Demo</c>, and the
/// page says so). Its content is invented here, and its session ends when the file <c>demo-signed-out</c> exists in the data folder, so that the whole chain, the mail of Mnemarr included, can
/// be seen without a real assistant.
/// </summary>
public sealed class DemoReader : IConversationReader
{
    public const string SignedOutFile = "demo-signed-out";

    /// <summary>The longest a demonstration conversation gets: a demonstration does not grow for ever.</summary>
    private const int MaxTurns = 1000;

    private const int StepsPerTurnOfTopics = 10;

    /// <summary>The file that remembers when the demonstration started: it keeps its conversations the same across a restart, and a new data folder starts them again.</summary>
    public const string StartedFile = "demo-started";

    private static readonly (string Title, string[] Sentences)[] Topics =
    [
        (
            "Demo: keeping a sourdough starter alive",
            [
                "How often should I feed a sourdough starter that lives in the fridge?",
                "Once a week is enough: take it out, discard most of it, and feed it equal weights of flour and water.",
                "Why does mine smell like nail polish remover?",
                "That is acetone from hunger: feed it twice a day for a few days and the smell goes back to a yeasty, sour one.",
                "Can I bake with it straight after feeding?",
                "Wait until it has doubled, usually four to eight hours at room temperature, then use it at its peak.",
            ]),
        (
            "Demo: a small Kubernetes cluster at home",
            [
                "I want to run three small services on a single node at home. Is Kubernetes too much?",
                "A lightweight distribution such as k3s runs fine on one node and gives you manifests, rollouts and a real ingress.",
                "How do I keep the configuration in git?",
                "Keep the manifests in a repository and let a GitOps controller apply them, with secrets encrypted before they are committed.",
                "What about backups of the volumes?",
                "Snapshot the volumes on a schedule and copy them off the machine; test a restore once, not only the backup.",
            ]),
        (
            "Demo: a long weekend in Lisbon",
            [
                "I have three days in Lisbon in March. What should I not miss?",
                "Walk through Alfama in the morning, take tram 28 outside rush hour, and spend an afternoon in Belem for the monastery and the pastries.",
                "Is it worth a day trip to Sintra?",
                "Yes, but start early: the palaces fill up by eleven, and the train from Rossio takes forty minutes.",
                "Where should we eat on the last evening?",
                "A small tasca in Graca with grilled fish and a jug of the house white is hard to beat.",
            ]),
    ];

    private readonly TimeProvider _clock;
    private readonly string _dataFolder;
    private readonly int _growSeconds;
    private readonly Lock _gate = new();
    private DateTimeOffset? _epoch;

    public DemoReader(TimeProvider clock, IOptions<GeminarrOptions> geminarr, IOptions<ReaderOptions> reader)
    {
        _clock = clock;
        _dataFolder = geminarr.Value.DataFolder;
        _growSeconds = reader.Value.DemoGrowSeconds;
    }

    public Task<SessionCheck> CheckSessionAsync(CancellationToken cancellationToken) =>
        Task.FromResult(File.Exists(Path.Combine(_dataFolder, SignedOutFile)) ? SessionCheck.SignedOut : SessionCheck.SignedIn);

    public Task<IReadOnlyList<ConversationHeader>> ListRecentAsync(int limit, CancellationToken cancellationToken)
    {
        var active = ActiveTopic();
        var headers = Enumerable.Range(0, Topics.Length)
            .OrderBy(topic => topic == active ? 0 : topic + 1)
            .Select((topic, rank) => new ConversationHeader(IdOf(topic), Topics[topic].Title, rank))
            .Take(limit)
            .ToList();
        return Task.FromResult<IReadOnlyList<ConversationHeader>>(headers);
    }

    public Task<IReadOnlyList<Turn>> OpenAsync(string id, CancellationToken cancellationToken)
    {
        var topic = Enumerable.Range(0, Topics.Length).FirstOrDefault(candidate => IdOf(candidate) == id, -1);
        if (topic < 0)
        {
            throw new ReaderException(Connection.ConnectionReasons.ReaderBroken, "The demonstration conversation is not there.");
        }

        var sentences = Topics[topic].Sentences;
        var count = Math.Min(MaxTurns, sentences.Length + (2 * StepsActive(topic)));
        IReadOnlyList<Turn> turns =
        [
            .. Enumerable.Range(0, count).Select(index => new Turn(
                index % 2 == 0 ? "user" : "assistant",
                Epoch.AddMinutes(index),
                index < sentences.Length ? sentences[index] : $"{sentences[index % sentences.Length]} (follow-up {index / sentences.Length + 1})")),
        ];
        return Task.FromResult(turns);
    }

    private static string IdOf(int topic) => $"demo-c{topic + 1}a2b3c4d5e6";

    /// <summary>When the demonstration started: written the first time it is needed.</summary>
    private DateTimeOffset Epoch
    {
        get
        {
            lock (_gate)
            {
                return _epoch ??= ReadOrWriteEpoch();
            }
        }
    }

    private DateTimeOffset ReadOrWriteEpoch()
    {
        var file = Path.Combine(_dataFolder, StartedFile);
        if (File.Exists(file) && DateTimeOffset.TryParse(File.ReadAllText(file), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var written))
        {
            return written;
        }

        var now = _clock.GetUtcNow();
        File.WriteAllText(file, now.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        return now;
    }

    private long Steps() => (long)((_clock.GetUtcNow() - Epoch).TotalSeconds / _growSeconds);

    /// <summary>The conversation that is being talked in: it changes every ten growth steps, so that the list moves.</summary>
    private int ActiveTopic() => (int)(Steps() / StepsPerTurnOfTopics % Topics.Length);

    /// <summary>How many growth steps a topic has been the active one: a conversation only ever grows.</summary>
    private int StepsActive(int topic)
    {
        var steps = Steps();
        var cycle = StepsPerTurnOfTopics * Topics.Length;
        return (int)((steps / cycle * StepsPerTurnOfTopics) + Math.Clamp((steps % cycle) - (StepsPerTurnOfTopics * topic), 0, StepsPerTurnOfTopics));
    }
}
