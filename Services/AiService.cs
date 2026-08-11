using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using raft_backend.Configuration;
using raft_backend.DTOs;

namespace raft_backend.Services;

public class AiService : IAiService
{
    private readonly IAiApiKeyService _apiKeyService;
    private readonly AiServiceOptions _options;
    private readonly ILogger<AiService> _logger;

    public AiService(
        IAiApiKeyService apiKeyService,
        IOptions<AiServiceOptions> options,
        ILogger<AiService> logger)
    {
        _apiKeyService = apiKeyService;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<AiGenerateResponseDto?> GenerateAsync(string apiKeySecret, AiGenerateRequestDto request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKeySecret))
        {
            return null;
        }

        var resolvedKey = await _apiKeyService.ResolveBySecretAsync(apiKeySecret, cancellationToken);
        if (resolvedKey is null || !string.Equals(resolvedKey.Status, "Active", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var mode = NormalizeMode(request.Mode);
        var result = await GenerateTextAsync(mode, request.Provider, request.Prompt, request.Context, cancellationToken);
        var promptTokens = result.PromptTokens > 0
            ? result.PromptTokens
            : EstimateTokens(request.Prompt + " " + (request.Context ?? string.Empty));
        var completionTokens = result.CompletionTokens > 0
            ? result.CompletionTokens
            : EstimateTokens(result.Text);
        var totalTokens = result.TotalTokens > 0
            ? result.TotalTokens
            : promptTokens + completionTokens;
        var approxCostUsd = result.ApproxCostUsd;

        await _apiKeyService.RecordUsageAsync(
            resolvedKey.Id,
            promptTokens,
            completionTokens,
            approxCostUsd,
            cancellationToken);

        return new AiGenerateResponseDto
        {
            Provider = result.Provider,
            Model = result.Model,
            Mode = mode,
            KeyId = resolvedKey.Id,
            KeyPrefix = resolvedKey.KeyPrefix,
            Result = result.Text,
            PromptTokens = promptTokens,
            CompletionTokens = completionTokens,
            TotalTokens = totalTokens,
            ApproxCostUsd = approxCostUsd,
            CreatedAt = DateTime.UtcNow
        };
    }

    public async Task<JsonElement?> ProxyOpenAiChatCompletionAsync(string apiKeySecret, JsonElement requestPayload, string? preferredProvider = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKeySecret))
        {
            return null;
        }

        var resolvedKey = await _apiKeyService.ResolveBySecretAsync(apiKeySecret, cancellationToken);
        if (resolvedKey is null || !string.Equals(resolvedKey.Status, "Active", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var providerCandidates = ResolveProviderCandidates(preferredProvider);
        foreach (var provider in providerCandidates)
        {
            if (!provider.IsConfigured)
            {
                continue;
            }

            try
            {
                var result = await ForwardOpenAiRequestAsync(provider, requestPayload, cancellationToken);
                if (result is not null)
                {
                    var root = result.Value;
                    var promptTokens = TryGetInt64(root, "usage", "prompt_tokens");
                    var completionTokens = TryGetInt64(root, "usage", "completion_tokens");
                    var totalTokens = TryGetInt64(root, "usage", "total_tokens");
                    if (totalTokens == 0)
                    {
                        totalTokens = promptTokens + completionTokens;
                    }
                    var approxCostUsd = totalTokens > 0 ? Math.Round(totalTokens / 1000m * 0.002m, 6) : 0m;

                    await _apiKeyService.RecordUsageAsync(
                        resolvedKey.Id,
                        promptTokens,
                        completionTokens,
                        approxCostUsd,
                        cancellationToken);

                    return root;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AI provider {Provider} failed during OpenAI proxy request; trying next candidate", provider.Name);
            }
        }

        return null;
    }

    private async Task<JsonElement?> ForwardOpenAiRequestAsync(AiProviderOptions provider, JsonElement requestPayload, CancellationToken cancellationToken)
    {
        using var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(provider.RequestTimeoutSeconds > 0 ? provider.RequestTimeoutSeconds : _options.RequestTimeoutSeconds)
        };

        var apiKey = string.IsNullOrWhiteSpace(provider.ApiKey) ? _options.ApiKey : provider.ApiKey;
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        string rawJson;
        if (requestPayload.ValueKind == JsonValueKind.Object && !requestPayload.TryGetProperty("model", out var modelProp))
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(requestPayload.GetRawText()) ?? new();
            dict["model"] = string.IsNullOrWhiteSpace(provider.Model) ? _options.Model : provider.Model;
            rawJson = JsonSerializer.Serialize(dict);
        }
        else
        {
            rawJson = requestPayload.GetRawText();
        }

        using var response = await client.PostAsync(
            provider.Endpoint,
            new StringContent(rawJson, Encoding.UTF8, "application/json"),
            cancellationToken);

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Remote AI provider {Provider} returned HTTP {StatusCode}: {ResponseBody}", provider.Name, (int)response.StatusCode, responseBody);
        }

        using var document = JsonDocument.Parse(responseBody);
        return document.RootElement.Clone();
    }


    private async Task<AiGenerationResult> GenerateTextAsync(string mode, string? preferredProvider, string prompt, string? context, CancellationToken cancellationToken)
    {
        var providerCandidates = ResolveProviderCandidates(preferredProvider);
        foreach (var provider in providerCandidates)
        {
            if (!provider.IsConfigured)
            {
                continue;
            }

            try
            {
                return await CallExternalProviderAsync(provider, mode, prompt, context, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AI provider {Provider} failed; trying next candidate", provider.Name);
            }
        }

        return BuildLocalResponse(mode, prompt, context);
    }

    private async Task<AiGenerationResult> CallExternalProviderAsync(AiProviderOptions provider, string mode, string prompt, string? context, CancellationToken cancellationToken)
    {
        using var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(provider.RequestTimeoutSeconds > 0 ? provider.RequestTimeoutSeconds : _options.RequestTimeoutSeconds)
        };

        var apiKey = string.IsNullOrWhiteSpace(provider.ApiKey) ? _options.ApiKey : provider.ApiKey;
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        var systemPrompt = BuildSystemPrompt(mode);
        var payload = new
        {
            model = string.IsNullOrWhiteSpace(provider.Model) ? _options.Model : provider.Model,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = BuildUserPrompt(mode, prompt, context) }
            },
            max_tokens = provider.MaxOutputTokens > 0 ? provider.MaxOutputTokens : _options.MaxOutputTokens,
            temperature = 0.3
        };

        using var response = await client.PostAsync(
            provider.Endpoint,
            new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
            cancellationToken);

        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var root = document.RootElement;
        var text = ExtractTextFromOpenAiCompatibleResponse(root) ?? BuildLocalResponse(mode, prompt, context).Text;
        var model = root.TryGetProperty("model", out var modelProperty) && modelProperty.ValueKind == JsonValueKind.String
            ? modelProperty.GetString() ?? provider.Model
            : provider.Model;

        var promptTokens = TryGetInt64(root, "usage", "prompt_tokens");
        var completionTokens = TryGetInt64(root, "usage", "completion_tokens");
        var totalTokens = TryGetInt64(root, "usage", "total_tokens");
        var approxCostUsd = totalTokens > 0 ? Math.Round(totalTokens / 1000m * 0.002m, 6) : 0m;

        return new AiGenerationResult(provider.Name, model, text, promptTokens, completionTokens, totalTokens, approxCostUsd);
    }

    private static string? ExtractTextFromOpenAiCompatibleResponse(JsonElement root)
    {
        if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
        {
            return null;
        }

        var choice = choices[0];
        if (choice.TryGetProperty("message", out var message) &&
            message.TryGetProperty("content", out var content) &&
            content.ValueKind == JsonValueKind.String)
        {
            return content.GetString();
        }

        if (choice.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
        {
            return text.GetString();
        }

        return null;
    }

    private static long TryGetInt64(JsonElement root, string containerName, string propertyName)
    {
        if (!root.TryGetProperty(containerName, out var container))
        {
            return 0;
        }

        return container.TryGetProperty(propertyName, out var property) && property.TryGetInt64(out var value)
            ? value
            : 0;
    }

    private static string NormalizeMode(string? mode)
    {
        var normalized = (mode ?? "general").Trim().ToLowerInvariant();
        return normalized switch
        {
            "sql" => "sql",
            "sql-help" => "sql",
            "summary" => "summary",
            "recommendation" => "recommendation",
            _ => "general"
        };
    }

    private static string BuildSystemPrompt(string mode)
    {
        return mode switch
        {
            "sql" => "Eres un asistente que ayuda a redactar, corregir y explicar SQL para estudiantes.",
            "summary" => "Eres un asistente que resume texto de forma clara y breve.",
            "recommendation" => "Eres un asistente que ofrece recomendaciones prácticas y concretas.",
            _ => "Eres un asistente útil, conciso y orientado a resolver tareas técnicas."
        };
    }

    private static string BuildUserPrompt(string mode, string prompt, string? context)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Modo: {mode}");
        if (!string.IsNullOrWhiteSpace(context))
        {
            builder.AppendLine("Contexto:");
            builder.AppendLine(context);
        }

        builder.AppendLine("Solicitud:");
        builder.AppendLine(prompt);
        return builder.ToString();
    }

    private static AiGenerationResult BuildLocalResponse(string mode, string prompt, string? context)
    {
        var text = mode switch
        {
            "sql" => BuildSqlHelp(prompt, context),
            "summary" => BuildSummary(prompt, context),
            "recommendation" => BuildRecommendation(prompt, context),
            _ => BuildGeneralResponse(prompt, context)
        };

        var promptTokens = EstimateTokens(prompt + " " + (context ?? string.Empty));
        var completionTokens = EstimateTokens(text);
        return new AiGenerationResult("local", "heuristic-ai", text, promptTokens, completionTokens, promptTokens + completionTokens, 0m);
    }

    private IReadOnlyList<AiProviderOptions> ResolveProviderCandidates(string? preferredProvider)
    {
        var configuredProviders = _options.Providers
            .Where(provider => provider is not null && provider.IsConfigured)
            .Select(provider => NormalizeProvider(provider))
            .OrderBy(provider => provider.Priority)
            .ThenBy(provider => provider.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (configuredProviders.Count == 0)
        {
            var legacy = CreateLegacyProvider();
            return legacy is null ? Array.Empty<AiProviderOptions>() : [legacy];
        }

        if (!string.IsNullOrWhiteSpace(preferredProvider))
        {
            var requested = configuredProviders.FirstOrDefault(provider => string.Equals(provider.Name, preferredProvider.Trim(), StringComparison.OrdinalIgnoreCase));
            if (requested is not null)
            {
                configuredProviders.RemoveAll(provider => string.Equals(provider.Name, requested.Name, StringComparison.OrdinalIgnoreCase));
                configuredProviders.Insert(0, requested);
            }
        }

        return configuredProviders;
    }

    private AiProviderOptions? CreateLegacyProvider()
    {
        if (string.IsNullOrWhiteSpace(_options.Endpoint))
        {
            return null;
        }

        return new AiProviderOptions
        {
            Name = "legacy",
            Endpoint = _options.Endpoint,
            ApiKey = _options.ApiKey,
            Model = _options.Model,
            Priority = 0,
            RequestTimeoutSeconds = _options.RequestTimeoutSeconds,
            MaxOutputTokens = _options.MaxOutputTokens
        };
    }

    private static AiProviderOptions NormalizeProvider(AiProviderOptions provider)
    {
        provider.Name = provider.Name.Trim();
        provider.Endpoint = provider.Endpoint.Trim();
        provider.Model = string.IsNullOrWhiteSpace(provider.Model) ? "gpt-4o-mini" : provider.Model.Trim();
        return provider;
    }

    private static string BuildSqlHelp(string prompt, string? context)
    {
        var lowerPrompt = prompt.ToLowerInvariant();

        if (lowerPrompt.Contains("users") || lowerPrompt.Contains("usuarios"))
        {
            return """
            Puedes usar una consulta como esta:

            SELECT u.Id, u.Name, u.Email, COUNT(d.Id) AS DatabaseCount
            FROM Users u
            LEFT JOIN DatabaseInstances d ON d.UserId = u.Id AND d.Deleted_at IS NULL
            WHERE u.Deleted_at IS NULL
            GROUP BY u.Id, u.Name, u.Email
            ORDER BY DatabaseCount DESC;
            """;
        }

        if (lowerPrompt.Contains("database") || lowerPrompt.Contains("base de datos"))
        {
            return """
            Podrías consultar el estado de las instancias con:

            SELECT Id, UserId, DatabaseName, Engine, Status, UsedSpaceBytes, MaxSpaceBytes
            FROM DatabaseInstances
            WHERE Deleted_at IS NULL
              AND Status = 'Active'
            ORDER BY Created_at DESC;
            """;
        }

        return $"""
        SQL helper:
        - Entendí tu solicitud: {prompt}
        - Contexto: {context ?? "sin contexto adicional"}
        - Sugerencia: define claramente tablas, filtros y campos de salida antes de escribir la consulta.
        """;
    }

    private static string BuildSummary(string prompt, string? context)
    {
        var text = string.Join(' ', new[] { prompt, context ?? string.Empty }.Where(value => !string.IsNullOrWhiteSpace(value)));
        var sentences = text
            .Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(3);

        return string.Join(". ", sentences) + (sentences.Any() ? "." : string.Empty);
    }

    private static string BuildRecommendation(string prompt, string? context)
    {
        return $"""
        Recomendaciones basadas en tu solicitud:
        1. Aterriza el objetivo principal de "{prompt}" en una sola frase.
        2. Define entradas, salidas y validaciones antes de implementar.
        3. Si el flujo involucra datos sensibles, persiste el estado y audita cada operación.
        4. Usa una interfaz simple para probar el flujo extremo a extremo.
        """;
    }

    private static string BuildGeneralResponse(string prompt, string? context)
    {
        return $"""
        Puedo ayudarte con tu solicitud.

        Solicitud: {prompt}
        {(!string.IsNullOrWhiteSpace(context) ? $"Contexto: {context}" : string.Empty)}

        Si quieres, te preparo una versión más concreta orientada a SQL, documentación o implementación.
        """;
    }

    private static int EstimateTokens(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 1;
        }

        return Math.Max(1, (int)Math.Ceiling(text.Length / 4.0));
    }

    private sealed record AiGenerationResult(
        string Provider,
        string Model,
        string Text,
        long PromptTokens,
        long CompletionTokens,
        long TotalTokens,
        decimal ApproxCostUsd);
}
