using System.IO;
using AI.Extension.Chat;
using AI.Extension.Providers;
using Microsoft.Extensions.AI;

namespace Demo.Tests;

/// <summary>供应商/模型管理：持久化加密、校验、当前模型切换、客户端访问器。</summary>
public sealed class ProviderManagementTests
{
    [Fact]
    public async Task JsonStore_EncryptsApiKeyAtRest()
    {
        string directory = Path.Combine(Path.GetTempPath(), "karl-ai-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string file = Path.Combine(directory, "providers.json");

        try
        {
            var store = new JsonAiProviderStore(file);
            var provider = new AiProviderDefinition
            {
                Id = "deepseek",
                DisplayName = "DeepSeek",
                Endpoint = "https://api.deepseek.com/v1",
                ApiKey = "sk-test-secret-123",
                Enabled = true,
                Models = [new AiModelDefinition { Id = "deepseek-chat" }],
                DefaultModelId = "deepseek-chat",
            };

            await store.SaveAsync(new AiProviderState([provider], "deepseek", "deepseek-chat"));

            string json = await File.ReadAllTextAsync(file);
            Assert.DoesNotContain("sk-test-secret-123", json, StringComparison.Ordinal);
            Assert.Contains("dpapi:", json, StringComparison.Ordinal);

            AiProviderState loaded = await store.LoadAsync();
            AiProviderDefinition loadedProvider = Assert.Single(loaded.Providers);
            Assert.Equal("sk-test-secret-123", loadedProvider.ApiKey);
            Assert.Equal("deepseek", loaded.ActiveProviderId);
            Assert.Equal("deepseek-chat", loaded.ActiveModelId);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task JsonStore_MissingFile_ReturnsEmptyState()
    {
        var store = new JsonAiProviderStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing.json"));

        AiProviderState state = await store.LoadAsync();

        Assert.Empty(state.Providers);
        Assert.Null(state.ActiveProviderId);
    }

    [Fact]
    public async Task Manager_FirstRun_SeedsPresetsWithoutActiveModel()
    {
        var manager = new AiProviderManager(new InMemoryAiProviderStore());

        await manager.InitializeAsync();

        Assert.True(manager.Providers.Count >= 4);
        Assert.Contains(manager.Providers, provider => provider.Id == "deepseek");
        Assert.Contains(manager.Providers, provider => provider.Id == "ollama" && !provider.RequiresApiKey);
        Assert.False(manager.HasActiveModel);
        Assert.Equal("未配置（离线演示）", manager.ActiveModelLabel);
    }

    [Fact]
    public async Task Manager_Save_RequiresApiKeyWhenEnabling()
    {
        AiProviderManager manager = await CreateManagerAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => manager.SaveAsync(new AiProviderDefinition
        {
            Id = "x",
            DisplayName = "X",
            Endpoint = "https://api.example.com/v1",
            Enabled = true,
            Models = [new AiModelDefinition { Id = "m" }],
        }));
    }

    [Fact]
    public async Task Manager_Save_NormalizesAndPersists()
    {
        var store = new InMemoryAiProviderStore();
        var manager = new AiProviderManager(store, seedProviders: []);
        await manager.InitializeAsync();

        AiProviderDefinition saved = await manager.SaveAsync(new AiProviderDefinition
        {
            Id = " deepseek ",
            DisplayName = " DeepSeek ",
            Endpoint = "https://api.deepseek.com/v1/",
            ApiKey = " sk-1 ",
            Enabled = true,
            Models = [new AiModelDefinition { Id = "deepseek-chat" }],
            DefaultModelId = "not-exists",
        });

        Assert.Equal("deepseek", saved.Id);
        Assert.Equal("DeepSeek", saved.DisplayName);
        Assert.Equal("https://api.deepseek.com/v1", saved.Endpoint);
        Assert.Equal("sk-1", saved.ApiKey);
        Assert.Equal("deepseek-chat", saved.DefaultModelId);

        AiProviderDefinition persisted = Assert.Single(store.CurrentState.Providers);
        Assert.Equal("sk-1", persisted.ApiKey);
    }

    [Fact]
    public async Task Manager_SetActive_SwitchesAccessorClientAndLabel()
    {
        var manager = new AiProviderManager(new InMemoryAiProviderStore());
        await manager.InitializeAsync();

        await manager.SaveAsync(new AiProviderDefinition
        {
            Id = "fake",
            DisplayName = "FakeProvider",
            Endpoint = "https://api.example.invalid/v1",
            ApiKey = "test-key",
            Enabled = true,
            Models = [new AiModelDefinition { Id = "model-a" }],
            DefaultModelId = "model-a",
        });

        var fallback = new UnconfiguredChatClient();
        using var accessor = new AiChatClientAccessor(manager, fallback);

        // 首个可用供应商保存后会自动成为当前模型；这里显式清除，验证回退兜底客户端
        await manager.SetActiveAsync(null, null);
        Assert.Same(fallback, accessor.Current);

        bool changed = false;
        accessor.Changed += (_, _) => changed = true;

        await manager.SetActiveAsync("fake", "model-a");

        Assert.True(changed);
        Assert.Equal("FakeProvider · model-a", manager.ActiveModelLabel);
        Assert.Contains("FakeProvider", accessor.DisplayName, StringComparison.Ordinal);
        Assert.NotSame(fallback, accessor.Current);

        await manager.SetActiveAsync(null, null);

        Assert.Same(fallback, accessor.Current);
        Assert.Contains("离线", accessor.DisplayName, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Manager_RemoveActiveProvider_FallsBackToNoModel()
    {
        var store = new InMemoryAiProviderStore();
        var manager = new AiProviderManager(store, seedProviders: []);
        await manager.InitializeAsync();

        await manager.SaveAsync(new AiProviderDefinition
        {
            Id = "only",
            DisplayName = "Only",
            Endpoint = "https://api.example.invalid/v1",
            ApiKey = "k",
            Enabled = true,
            Models = [new AiModelDefinition { Id = "m" }],
            DefaultModelId = "m",
        });
        await manager.SetActiveAsync("only", "m");
        Assert.True(manager.HasActiveModel);

        await manager.RemoveAsync("only");

        Assert.Empty(manager.Providers);
        Assert.False(manager.HasActiveModel);
        Assert.Empty(store.CurrentState.Providers);
    }

    [Fact]
    public async Task Manager_MigratesOutdatedDeepSeekPreset_KeepingKeyAndEnabledState()
    {
        // 模拟旧版本安装：DeepSeek 指向旧的 /v1 + 已下线模型，用户已填 Key 并启用
        var outdated = new AiProviderDefinition
        {
            Id = "deepseek",
            DisplayName = "DeepSeek",
            Endpoint = "https://api.deepseek.com/v1",
            ApiKey = "sk-legacy-key",
            Enabled = true,
            DefaultModelId = "deepseek-chat",
            Models = [new AiModelDefinition { Id = "deepseek-chat" }, new AiModelDefinition { Id = "deepseek-reasoner" }],
        };

        var store = new InMemoryAiProviderStore(new AiProviderState([outdated], "deepseek", "deepseek-chat", Version: 0));
        var manager = new AiProviderManager(store, seedProviders: []);

        await manager.InitializeAsync();

        AiProviderDefinition migrated = Assert.Single(manager.Providers);
        Assert.Equal("https://api.deepseek.com", migrated.Endpoint);
        Assert.Equal("sk-legacy-key", migrated.ApiKey);
        Assert.True(migrated.Enabled);
        Assert.Contains(migrated.Models, model => model.Id == "deepseek-flash");
        Assert.Contains(migrated.Models, model => model.Id == "deepseek-v4-pro");
        Assert.DoesNotContain(migrated.Models, model => model.Id == "deepseek-chat");
        Assert.Equal("deepseek-flash", manager.ActiveModel?.Id);
        Assert.Equal(2, store.CurrentState.Version);
    }

    private static async Task<AiProviderManager> CreateManagerAsync()
    {
        var manager = new AiProviderManager(new InMemoryAiProviderStore(), seedProviders: []);
        await manager.InitializeAsync();
        return manager;
    }
}

