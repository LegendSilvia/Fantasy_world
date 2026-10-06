using System.Net;
using System.Text.Json;
using FantasyWorld.Llm;
using FantasyWorld.Sim.Llm;

namespace FantasyWorld.Sim.Tests.Llm;

public class ProviderTests
{
    private sealed class Handler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = request;
            RequestBody = await request.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(status) { Content = new StringContent(body) };
        }
    }

    private static readonly LlmRequest Decide = new(ModelTier.Decide, "k", "THE BIBLE", "Choose.",
        "{\"type\":\"object\",\"properties\":{\"choice\":{\"type\":\"string\"}},\"required\":[\"choice\"],\"additionalProperties\":false}", 512);

    [Fact]
    public void AnthropicRequests_CacheTheBible_AndAskForSchemaJson()
    {
        var p = AnthropicProvider.BuildParams(Decide, "claude-opus-5-5");
        Assert.Equal(512 + AnthropicProvider.ThinkingHeadroom, p.MaxTokens);
        var json = JsonSerializer.Serialize(p);
        Assert.Contains("\"model\":\"claude-opus-5-5\"", json);
        Assert.Contains("\"cache_control\":{\"type\":\"ephemeral\"", json);
        Assert.Contains("\"effort\":\"low\"", json);
        Assert.Contains("\"type\":\"json_schema\"", json);
        Assert.Contains("THE BIBLE", json);
        Assert.Contains("\"effort\":\"medium\"", JsonSerializer.Serialize(AnthropicProvider.BuildParams(Decide with { Tier = ModelTier.Narrate }, "claude-opus-5-5")));
    }

    [Fact]
    public async Task OpenRouter_SendsSchemaJson_AndReadsCost()
    {
        var handler = new Handler(HttpStatusCode.OK,
            "{\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"content\":\"{\\\"choice\\\":\\\"a\\\"}\"}}],\"usage\":{\"prompt_tokens\":900,\"completion_tokens\":40,\"cost\":0.0021}}");
        var provider = new OpenRouterProvider("sk-test", new HttpClient(handler));

        var response = await provider.CompleteJsonAsync(Decide, "anthropic/claude-opus-5-5");

        Assert.Equal("{\"choice\":\"a\"}", response.Json);
        Assert.Equal((900, 40, 0.0021m), (response.InputTokens, response.OutputTokens, response.CostUsd));
        Assert.Equal("Bearer sk-test", handler.Request!.Headers.Authorization!.ToString());
        var body = JsonDocument.Parse(handler.RequestBody!).RootElement;
        Assert.Equal("json_schema", body.GetProperty("response_format").GetProperty("type").GetString());
        Assert.Equal("THE BIBLE", body.GetProperty("messages")[0].GetProperty("content").GetString());
    }

    [Fact]
    public async Task OpenRouterErrors_AreUnavailability()
    {
        var provider = new OpenRouterProvider("sk-test", new HttpClient(new Handler(HttpStatusCode.TooManyRequests, "{}")));
        await Assert.ThrowsAsync<LlmUnavailableException>(() => provider.CompleteJsonAsync(Decide, "m"));
        var garbled = new OpenRouterProvider("sk-test", new HttpClient(new Handler(HttpStatusCode.OK, "not json")));
        await Assert.ThrowsAsync<LlmUnavailableException>(() => garbled.CompleteJsonAsync(Decide, "m"));
    }

    [Fact]
    public void Prices_CoverTheLedger_AndUnknownModelsErrHigh()
    {
        Assert.Equal(0.0042m, Price.For("claude-opus-5-5").Cost(1000, 0, 0, 0) + Price.For("claude-opus-5-5").Cost(0, 0, 1000, 0));
        Assert.Equal(Price.For("claude-opus-5-5"), Price.For("anthropic/claude-opus-5-5"));
        Assert.True(Price.For("mystery-model").Output >= Price.For("claude-opus-5-5").Output);
    }

    [Fact]
    public void Providers_FollowTheSettings_AndNeedCredentials()
    {
        Assert.Null(LlmProviders.Create(new LlmSettings()));
        Assert.Null(LlmProviders.Create(new LlmSettings { Provider = "anthropic" }, _ => null));
        Assert.IsType<AnthropicProvider>(LlmProviders.Create(new LlmSettings { Provider = "anthropic" }, v => v == "ANTHROPIC_API_KEY" ? "sk" : null));
        Assert.Null(LlmProviders.Create(new LlmSettings { Provider = "openrouter" }, _ => null));
        Assert.IsType<OpenRouterProvider>(LlmProviders.Create(new LlmSettings { Provider = "openrouter" }, _ => "sk"));
    }

    [Fact]
    public async Task OpenRouter_RoutesClaudeModels_LeavesThinkingRoom_AndReportsRefusals()
    {
        Assert.Equal("anthropic/claude-opus-5-5", OpenRouterProvider.Route("claude-opus-5-5"));
        Assert.Equal("openai/x", OpenRouterProvider.Route("openai/x"));
        var body = OpenRouterProvider.BuildBody(Decide, "claude-opus-5-5");
        Assert.Equal(512 + AnthropicProvider.ThinkingHeadroom, (int)body["max_tokens"]!);

        var filtered = new OpenRouterProvider("sk", new HttpClient(new Handler(HttpStatusCode.OK,
            "{\"choices\":[{\"finish_reason\":\"content_filter\",\"message\":{\"content\":\"\"}}],\"usage\":{\"prompt_tokens\":9,\"completion_tokens\":0,\"cost\":0.001}}")));
        var refused = await Assert.ThrowsAsync<LlmRefusedException>(() => filtered.CompleteJsonAsync(Decide, "claude-opus-5-5"));
        Assert.Equal(0.001m, refused.Response!.CostUsd);
    }
}
