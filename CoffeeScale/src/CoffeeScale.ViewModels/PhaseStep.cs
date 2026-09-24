using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CoffeeScale.ViewModels;

/// <summary>
/// 冲煮流程节点：由引擎若干相邻阶段归并而来。
/// 例如「闷蒸注水 + 闷蒸等待」合并为「闷蒸」，「第N段注水 + 等待下降」合并为「注水N」。
/// 节点数量与标题随推荐方案动态生成，不写死。
/// 2026-09-03：UI 管道图已取消，IsLast/State/Badge 三个仅被渲染层使用的属性已删除；
/// 保留 Title/Sub/Icon/FromPhase/ToPhase/Done/Current（NextPhaseText 预告行依赖）。
/// </summary>
public sealed class PhaseStep : INotifyPropertyChanged
{
    private bool _done;
    private bool _current;

    public PhaseStep(int index, string title, string sub, string icon, int fromPhase, int toPhase)
    {
        Index = index; Title = title; Sub = sub; Icon = icon;
        FromPhase = fromPhase; ToPhase = toPhase;
    }

    /// <summary>节点序号（1 起）。</summary>
    public int Index { get; }
    /// <summary>节点标题（闷蒸 / 注水1 / 冲煮结束…）。</summary>
    public string Title { get; }
    /// <summary>节点图形化图标（emoji），如 🌸 闷蒸 / 💧 注水 / ☕ 冲煮结束。</summary>
    public string Icon { get; }
    /// <summary>副标题（目标重量 + 等待秒数，如「→ 120g · 15s」）。</summary>
    public string Sub { get; }
    /// <summary>本节点覆盖的引擎阶段起始索引（含）。</summary>
    public int FromPhase { get; }
    /// <summary>本节点覆盖的引擎阶段结束索引（含）。</summary>
    public int ToPhase { get; }

    /// <summary>已完成（当前阶段已越过本节点）。</summary>
    public bool Done
    {
        get => _done;
        set
        {
            if (_done == value) return;
            _done = value;
            Raise(nameof(Done));
        }
    }

    /// <summary>进行中（当前阶段落在本节点区间内）。</summary>
    public bool Current
    {
        get => _current;
        set
        {
            if (_current == value) return;
            _current = value;
            Raise(nameof(Current));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
