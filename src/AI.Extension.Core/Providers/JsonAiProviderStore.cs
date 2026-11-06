using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace AI.Extension.Providers;

/// <summary>
/// JSON 文件实现：默认位于 <c>%APPDATA%\Karl.AiExtension\providers.json</c>,
/// 可用环境变量 <c>KARL_AI_DATA_DIR</c> 覆盖数据目录（测试/绿色版场景）。
/// API Key 写入文件前经 <see cref="IAiSecretProtector"/> 加密。
/// </summary>
public sealed class JsonAiProviderStore : IAiProviderStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _filePath;
    private readonly IAiSecretProtector _protector;

    /// <summary>构造存储。</summary>
    /// <param name="filePath">文件路径；为空时使用默认路径。</param>
    /// <param name="protector">密钥保护器；为空时使用 DPAPI 实现。</param>
    public JsonAiProviderStore(string? filePath = null, IAiSecretProtector? protector = null)
    {
        _filePath = string.IsNullOrWhiteSpace(filePath) ? GetDefaultPath() : filePath;
        _protector = protector ?? new DpapiSecretProtector();
    }

    /// <summary>默认配置文件路径。</summary>
    public static string GetDefaultPath()
    {
        string? customDirectory = Environment.GetEnvironmentVariable("KARL_AI_DATA_DIR");
        string directory = string.IsNullOrWhiteSpace(customDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Karl.AiExtension")
            : customDirectory!;

        return Path.Combine(directory, "providers.json");
    }

    /// <inheritdoc />
    public async Task<AiProviderState> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_filePath))
            {
                return AiProviderState.Empty;
            }

            string json = await File.ReadAllTextAsync(_filePath, cancellationToken).ConfigureAwait(false);
            ProviderStateDto? dto = JsonSerializer.Deserialize<ProviderStateDto>(json, SerializerOptions);

            return dto is null ? AiProviderState.Empty : ToState(dto);
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            // 配置损坏不应导致应用起不来：按空配置处理，下次保存会覆盖
            return AiProviderState.Empty;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task SaveAsync(AiProviderState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string? directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string json = JsonSerializer.Serialize(ToDto(state), SerializerOptions);
            string temporaryPath = _filePath + ".tmp";
            await File.WriteAllTextAsync(temporaryPath, json, cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, _filePath, overwrite: true);
        }
        finally
        {
            _gate.Release();
        }
    }

    private AiProviderState ToState(ProviderStateDto dto) => new(
        dto.Providers
            .Where(static provider => !string.IsNullOrWhiteSpace(provider.Id))
            .Select(provider => new AiProviderDefinition
            {
                Id = provider.Id,
                DisplayName = string.IsNullOrWhiteSpace(provider.DisplayName) ? provider.Id : provider.DisplayName,
                Kind = Enum.TryParse(provider.Kind, ignoreCase: true, out AiProviderKind kind)
                    ? kind
                    : AiProviderKind.OpenAICompatible,
                Endpoint = provider.Endpoint ?? string.Empty,
                ApiKey = _protector.Unprotect(provider.ApiKeyProtected),
                Enabled = provider.Enabled,
                RequiresApiKey = provider.RequiresApiKey,
                Models = (provider.Models ?? new List<ModelDto>())
                    .Where(static model => !string.IsNullOrWhiteSpace(model.Id))
                    .Select(static model => new AiModelDefinition { Id = model.Id!, DisplayName = model.DisplayName })
                    .ToArray(),
                DefaultModelId = provider.DefaultModelId,
                Notes = provider.Notes,
            })
            .ToArray(),
        string.IsNullOrWhiteSpace(dto.ActiveProviderId) ? null : dto.ActiveProviderId,
        string.IsNullOrWhiteSpace(dto.ActiveModelId) ? null : dto.ActiveModelId,
        dto.Version);

    private ProviderStateDto ToDto(AiProviderState state) => new()
    {
        Providers = state.Providers.Select(provider => new ProviderDto
        {
            Id = provider.Id,
            DisplayName = provider.DisplayName,
            Kind = provider.Kind.ToString(),
            Endpoint = provider.Endpoint,
            ApiKeyProtected = string.IsNullOrWhiteSpace(provider.ApiKey) ? null : _protector.Protect(provider.ApiKey),
            Enabled = provider.Enabled,
            RequiresApiKey = provider.RequiresApiKey,
            Models = provider.Models
                .Select(static model => new ModelDto { Id = model.Id, DisplayName = model.DisplayName })
                .ToList(),
            DefaultModelId = provider.DefaultModelId,
            Notes = provider.Notes,
        }).ToList(),
        ActiveProviderId = state.ActiveProviderId,
        ActiveModelId = state.ActiveModelId,
        Version = state.Version,
    };

    private sealed class ProviderStateDto
    {
        public List<ProviderDto> Providers { get; set; } = new();

        public string? ActiveProviderId { get; set; }

        public string? ActiveModelId { get; set; }

        public int Version { get; set; }
    }

    private sealed class ProviderDto
    {
        public string Id { get; set; } = string.Empty;

        public string? DisplayName { get; set; }

        public string? Kind { get; set; }

        public string? Endpoint { get; set; }

        public string? ApiKeyProtected { get; set; }

        public bool Enabled { get; set; } = true;

        public bool RequiresApiKey { get; set; } = true;

        public List<ModelDto>? Models { get; set; }

        public string? DefaultModelId { get; set; }

        public string? Notes { get; set; }
    }

    private sealed class ModelDto
    {
        public string Id { get; set; } = string.Empty;

        public string? DisplayName { get; set; }
    }
}

