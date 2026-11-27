#if NET48
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace AI.Extension;

/// <summary>
/// .NET Framework 强名称程序集解析兜底。
/// <para>
/// Karl 的依赖树里 <c>Microsoft.Extensions.*</c> 在 .NET Framework 上按 10.0.0.0 强名称互引，
/// 实际 DLL 是 10.0.0.14；老式宿主没有 bindingRedirect 时会抛
/// <see cref="FileNotFoundException"/>。
/// 这里按“插件/输出目录里的同名 DLL”做简单名兜底，不改宿主配置文件。
/// </para>
/// </summary>
public static class AiNet48Compatibility
{
    private static readonly object Sync = new();
    private static readonly Dictionary<string, Assembly?> Cache = new(StringComparer.OrdinalIgnoreCase);

    [ThreadStatic]
    private static bool _resolving;

    private static bool _installed;

    /// <summary>
    /// 安装 AppDomain.AssemblyResolve 兜底（幂等）。
    /// 插件应在初始化最早期调用一次；Abstractions 模块初始化时也会自动安装。
    /// </summary>
    public static void EnsureAssemblyResolver()
    {
        lock (Sync)
        {
            if (_installed)
            {
                return;
            }

            _installed = true;
            AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        }
    }

    [ModuleInitializer]
    internal static void Initialize() => EnsureAssemblyResolver();

    private static Assembly? Resolve(object? sender, ResolveEventArgs args)
    {
        if (_resolving)
        {
            return null;
        }

        try
        {
            var requested = new AssemblyName(args.Name);
            if (string.IsNullOrEmpty(requested.Name))
            {
                return null;
            }

            _resolving = true;
            try
            {
                lock (Sync)
                {
                    if (Cache.TryGetValue(requested.Name, out Assembly? cached))
                    {
                        return cached;
                    }

                    Assembly? resolved = FindLoaded(requested) ?? LoadFromOutput(requested);
                    Cache[requested.Name] = resolved;
                    return resolved;
                }
            }
            finally
            {
                _resolving = false;
            }
        }
        catch
        {
            return null;
        }
    }

    private static Assembly? FindLoaded(AssemblyName requested)
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            AssemblyName candidate = assembly.GetName();
            if (!string.Equals(candidate.Name, requested.Name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!IdentityMatches(candidate, requested))
            {
                continue;
            }

            if (CompareVersions(candidate.Version, requested.Version) >= 0)
            {
                return assembly;
            }
        }

        return null;
    }

    private static Assembly? LoadFromOutput(AssemblyName requested)
    {
        foreach (string directory in CandidateDirectories())
        {
            string path = Path.Combine(directory, requested.Name + ".dll");
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                Assembly assembly = Assembly.LoadFrom(path);
                AssemblyName candidate = assembly.GetName();

                if (!string.Equals(candidate.Name, requested.Name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!IdentityMatches(candidate, requested))
                {
                    continue;
                }

                if (CompareVersions(candidate.Version, requested.Version) >= 0)
                {
                    return assembly;
                }
            }
            catch
            {
                // 文件不可加载/签名不符：继续尝试下一个目录
            }
        }

        return null;
    }

    private static IEnumerable<string> CandidateDirectories()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        string? ownDirectory = Path.GetDirectoryName(typeof(AiNet48Compatibility).Assembly.Location);
        if (!string.IsNullOrEmpty(ownDirectory) && seen.Add(ownDirectory))
        {
            yield return ownDirectory;
        }

        string baseDirectory = AppContext.BaseDirectory;
        if (!string.IsNullOrEmpty(baseDirectory) && seen.Add(baseDirectory))
        {
            yield return baseDirectory;
        }

        string? entryDirectory = Path.GetDirectoryName(Assembly.GetEntryAssembly()?.Location);
        if (!string.IsNullOrEmpty(entryDirectory) && seen.Add(entryDirectory))
        {
            yield return entryDirectory;
        }
    }

    private static bool IdentityMatches(AssemblyName candidate, AssemblyName requested)
    {
        byte[]? requestedToken = requested.GetPublicKeyToken();
        if (requestedToken is null || requestedToken.Length == 0)
        {
            return true;
        }

        byte[]? candidateToken = candidate.GetPublicKeyToken();
        if (candidateToken is null || candidateToken.Length != requestedToken.Length)
        {
            return false;
        }

        for (int i = 0; i < requestedToken.Length; i++)
        {
            if (candidateToken[i] != requestedToken[i])
            {
                return false;
            }
        }

        return true;
    }

    private static int CompareVersions(Version? candidate, Version? requested)
    {
        if (requested is null)
        {
            return 1;
        }

        if (candidate is null)
        {
            return -1;
        }

        return candidate.CompareTo(requested);
    }
}
#endif
