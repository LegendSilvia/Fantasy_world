namespace FantasyWorld.Sim.Llm;

/// <summary>
/// The one door to the LLM (spec §8.4). Answers come from the journal first, so replays and reloads never
/// re-query. A new question is asked only when a provider is configured, the tier's rolling 24-hour budget
/// and the monthly spend cap allow it; an answer that fails validation is retried once. Any failure returns
/// null and the caller takes the offline path, so the game always runs; a provider that is unreachable is not
/// asked again this run. Rejected and refused answers are still billed, so they are journaled too (under their
/// own hash, never replayed) and count against the budget and cap.
/// </summary>
public sealed class LlmGateway
{
    private ILlmProvider? _provider;
    private readonly TimeProvider _time;

    public LlmGateway(ILlmProvider? provider, LlmSettings settings, LlmJournal journal, TimeProvider? time = null)
    {
        _provider = settings.Enabled ? provider : null;
        Settings = settings;
        Journal = journal;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>A gateway that only replays journaled answers (tests, verification, offline worlds).</summary>
    public static LlmGateway Offline(LlmJournal? journal = null) => new(null, new LlmSettings(), journal ?? new LlmJournal());

    public LlmSettings Settings { get; }
    public LlmJournal Journal { get; }
    public int Replayed { get; private set; }
    public int Asked { get; private set; }
    public int Fallbacks { get; private set; }

    /// <summary>Why the provider was dropped for this run, if it was.</summary>
    public string? Failure { get; private set; }

    public static bool IsStoryBeat(ModelTier tier) => tier != ModelTier.Narrate;

    /// <summary>A provider is configured, so new questions may be asked.</summary>
    public bool Online => _provider is not null;

    /// <summary>The answer to this question is already journaled.</summary>
    public bool Knows(ModelTier tier, string key) => Journal.Count > 0 && Journal.TryGet(LlmRequest.HashOf(tier + "|" + key), out _);

    /// <summary>Worth building a prompt for: the answer is journaled, or a provider may be asked within budget.</summary>
    public bool MayAnswer(ModelTier tier, string key) => Knows(tier, key) || Online && WithinBudget(tier);

    /// <summary>
    /// Returns a valid JSON answer, or null for the offline path. <paramref name="whyInvalid"/> says what was wrong with a
    /// rejected answer; it is kept in the journal's note ("rejected: …") so a failed answer can be diagnosed, and the retry is told.
    /// </summary>
    public string? Ask(LlmRequest request, long worldDay, Func<string, bool> isValid, Func<string, string>? noteFor = null,
        Func<string, string>? whyInvalid = null)
    {
        if (Journal.TryGet(request.Hash, out var known))
        {
            Replayed++;
            return known.Json;
        }
        if (_provider is null || !WithinBudget(request.Tier))
        {
            Fallbacks++;
            return null;
        }
        var asked = request;
        for (var attempt = 0; attempt < 2 && _provider is not null; attempt++)
        {
            LlmResponse response;
            try
            {
                response = _provider.CompleteJsonAsync(asked, Settings.ModelFor(request.Tier)).GetAwaiter().GetResult();
            }
            catch (LlmRefusedException refused)
            {
                Asked++;
                if (refused.Response is { } billed) Record(request, $"!refused{attempt}", worldDay, billed, "refused");
                break;
            }
            catch (Exception e)
            {
                // Unreachable, unauthorised, timed out or garbled: stay offline for the rest of this run.
                Failure = e.Message;
                _provider = null;
                break;
            }
            Asked++;
            if (!isValid(response.Json))
            {
                var why = whyInvalid?.Invoke(response.Json);
                Record(request, $"!rejected{attempt}", worldDay, response, string.IsNullOrEmpty(why) ? "rejected" : $"rejected: {why}");
                // The retry is told what was wrong, so it can mend it rather than repeat it (the question, and its hash, stay the same).
                if (!string.IsNullOrEmpty(why))
                    asked = request with { Prompt = request.Prompt + $"\n\nAn earlier answer was rejected for: {why}. Write it again without these." };
                continue;
            }
            Record(request, "", worldDay, response, noteFor?.Invoke(response.Json) ?? "");
            return response.Json;
        }
        Fallbacks++;
        return null;
    }

    private void Record(LlmRequest request, string suffix, long worldDay, LlmResponse response, string note) =>
        Journal.Add(new LlmCall(request.Hash + suffix, request.Tier, worldDay, response.Provider, response.Model,
            suffix.Length == 0 ? response.Json : "", response.InputTokens, response.OutputTokens, response.CacheReadTokens,
            response.CacheWriteTokens, response.CostUsd, _time.GetUtcNow(), note));

    private bool WithinBudget(ModelTier tier)
    {
        var now = _time.GetUtcNow();
        if (Journal.SpendSince(now.AddDays(-30)) >= Settings.MonthlySpendCapUsd) return false;
        var story = IsStoryBeat(tier);
        var used = Journal.CountSince(now.AddDays(-1), t => IsStoryBeat(t) == story);
        return used < (story ? Settings.StoryBeatsPerDay : Settings.NarrationsPerDay);
    }
}
