using System.Reflection;
using System.Text.Json;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;


    /// <summary>
    /// 已安装插件索引条目 —— 用于 installed.json 文件的序列化/反序列化。
    /// <para>
    /// 记录每个动态安装插件的关键信息，确保应用重启后能够恢复已安装插件。
    /// </para>
    /// </summary>
internal class InstalledPluginEntry
    {
        /// <summary>
        /// 插件类型标识，格式为 "{Category}.{PluginId}"，如 "LyricsProvider.NetEaseLyrics"
        /// </summary>
        public string PluginTypeId { get; set; } = string.Empty;

        /// <summary>
        /// 插件 DLL 文件的本地绝对路径
        /// </summary>
        public string? AssemblyPath { get; set; }

        /// <summary>
        /// 插件安装来源 URL（GitHub 安装时为仓库 URL，本地安装时为文件路径）
        /// </summary>
        public string? InstallUrl { get; set; }

        /// <summary>
        /// 插件显示名称，用于索引记录
        /// </summary>
        public string? PluginName { get; set; }
    }
