using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using N225BrokerBridge.Application.Signals;

namespace N225BrokerBridge.UI.ViewModels;

/// <summary>
/// 戦略管理ダイアログのビューモデル。
///
/// 操作シーケンス (旧 N225OrderBridge StrategyForm に近い):
///   1. 登録: アラート名/Interval/説明 を入力 → 「追加」ボタン
///   2. 削除: 行をクリック (青く反転) → 「削除」ボタン
///   3. 修正: 行をクリック → 「編集」ボタン (値が入力欄にコピー) → 修正 → 「更新」ボタン
///   - 編集中はフィールドが「編集中の値」を保持。「キャンセル」で破棄しフィールドクリア
/// </summary>
public sealed partial class StrategyManagerViewModel : ObservableObject
{
    private readonly IStrategyRegistry _registry;
    private readonly ILogger<StrategyManagerViewModel> _logger;

    public ObservableCollection<StrategyEditorRow> Entries { get; } = new();

    [ObservableProperty] private StrategyEditorRow? _selectedEntry;

    /// <summary>編集中フラグ。true のとき「更新」「キャンセル」が有効。「追加」「編集」「削除」は無効。</summary>
    [ObservableProperty] private bool _isEditing;

    /// <summary>編集対象の参照 (IsEditing=true の間、SelectedEntry が変わっても保持)。</summary>
    private StrategyEditorRow? _editingTarget;

    [ObservableProperty] private string _editingAlertName = string.Empty;
    // ui:NumberBox.Value が double? 型のため、ViewModel 側も double で保持する。
    // int で宣言すると double?→int の書き戻し変換が失敗し、UI で入力した Interval が反映されず
    // 初期値 5 のまま固定される (全戦略が「5分」になるバグ。MainViewModel.OrderQty と同一原因)。
    // StrategyEntry へ渡す際に int にキャストする (2026-06-08 修正)。
    [ObservableProperty] private double _editingInterval = 5;
    [ObservableProperty] private bool _editingIsEnabled;
    [ObservableProperty] private string _editingDescription = string.Empty;

    [ObservableProperty] private string _statusMessage = "ヒント: 入力 + 「追加」 / 行選択 + 「編集」「削除」";

    /// <summary>UI 表示用ラベル: 編集中の対象を明示。</summary>
    public string EditModeLabel => IsEditing && _editingTarget is not null
        ? $"編集中: {_editingTarget.AlertName}/{_editingTarget.Interval}m  (修正後「更新」で確定)"
        : SelectedEntry is not null
            ? $"選択中: {SelectedEntry.AlertName}/{SelectedEntry.Interval}m"
            : "入力欄に値を入れて「追加」、または一覧で行を選択";

    partial void OnSelectedEntryChanged(StrategyEditorRow? value)
    {
        // 行選択ではフィールドを触らない (「追加」用のフォームを保持するため)
        OnPropertyChanged(nameof(EditModeLabel));
        NotifyCommandsCanExecuteChanged();
    }

    partial void OnIsEditingChanged(bool value)
    {
        OnPropertyChanged(nameof(EditModeLabel));
        NotifyCommandsCanExecuteChanged();
    }

    private void NotifyCommandsCanExecuteChanged()
    {
        AddCommand.NotifyCanExecuteChanged();
        EditCommand.NotifyCanExecuteChanged();
        UpdateCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
    }

    public StrategyManagerViewModel(IStrategyRegistry registry, ILogger<StrategyManagerViewModel> logger)
    {
        _registry = registry;
        _logger = logger;
        ReloadEntries();
    }

    private void ReloadEntries()
    {
        Entries.Clear();
        foreach (var e in _registry.GetAll().OrderBy(e => e.AlertName).ThenBy(e => e.Interval))
        {
            Entries.Add(new StrategyEditorRow
            {
                AlertName = e.AlertName,
                Interval = e.Interval,
                IsEnabled = e.IsEnabled,
                Description = e.Description ?? string.Empty,
                LastSignalAt = e.LastSignalAt,
                LastTradeType = e.LastTradeType,
                LastSide = e.LastSide,
                LastPrice = e.LastPrice
            });
        }
    }

    private void ClearFields()
    {
        EditingAlertName = string.Empty;
        EditingInterval = 5;
        EditingIsEnabled = false;
        EditingDescription = string.Empty;
    }

    // ── 5 つのコマンド ─────────────────────────────────────

    /// <summary>追加: フィールドの値を新規エントリとして登録。編集中は無効。</summary>
    [RelayCommand(CanExecute = nameof(CanAdd))]
    private async Task Add()
    {
        if (string.IsNullOrWhiteSpace(EditingAlertName))
        {
            StatusMessage = "アラート名を入力してください";
            return;
        }
        if (EditingInterval <= 0)
        {
            StatusMessage = "Interval は 1 以上を指定してください";
            return;
        }
        var interval = (int)EditingInterval;
        var name = EditingAlertName.Trim();
        if (Entries.Any(e => e.AlertName == name && e.Interval == interval))
        {
            StatusMessage = $"既に登録済み: {name}/{interval}m";
            return;
        }
        try
        {
            await _registry.UpsertAsync(new StrategyEntry
            {
                AlertName = name,
                Interval = interval,
                IsEnabled = EditingIsEnabled,
                Description = EditingDescription
            });
            StatusMessage = $"追加: {name}/{interval}m";
            _logger.LogInformation("Strategy added: {Name}/{Interval}m", name, interval);
            ClearFields();
            ReloadEntries();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add strategy");
            StatusMessage = $"追加エラー: {ex.Message}";
        }
    }
    private bool CanAdd() => !IsEditing;

    /// <summary>編集: 選択行の値をフィールドにコピーして編集モード ON。</summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void Edit()
    {
        if (SelectedEntry is null) return;
        _editingTarget = SelectedEntry;
        EditingAlertName = SelectedEntry.AlertName;
        EditingInterval = SelectedEntry.Interval;
        EditingIsEnabled = SelectedEntry.IsEnabled;
        EditingDescription = SelectedEntry.Description;
        IsEditing = true;
        StatusMessage = $"編集モード: {SelectedEntry.AlertName}/{SelectedEntry.Interval}m";
    }
    private bool CanEdit() => !IsEditing && SelectedEntry is not null;

    /// <summary>更新: 編集モードのみ動作。主キー変更時は旧削除+新追加。</summary>
    [RelayCommand(CanExecute = nameof(CanUpdate))]
    private async Task Update()
    {
        if (!IsEditing || _editingTarget is null) return;
        if (string.IsNullOrWhiteSpace(EditingAlertName))
        {
            StatusMessage = "アラート名は空にできません";
            return;
        }
        if (EditingInterval <= 0)
        {
            StatusMessage = "Interval は 1 以上を指定してください";
            return;
        }

        var oldName = _editingTarget.AlertName;
        var oldInterval = _editingTarget.Interval;
        var newName = EditingAlertName.Trim();
        var newInterval = (int)EditingInterval;
        var keyChanged = (oldName != newName) || (oldInterval != newInterval);

        if (keyChanged && Entries.Any(e => !ReferenceEquals(e, _editingTarget)
                                        && e.AlertName == newName
                                        && e.Interval == newInterval))
        {
            StatusMessage = $"既に登録済みのキー: {newName}/{newInterval}m";
            return;
        }

        try
        {
            if (keyChanged)
            {
                await _registry.RemoveAsync(oldName, oldInterval);
            }
            await _registry.UpsertAsync(new StrategyEntry
            {
                AlertName = newName,
                Interval = newInterval,
                IsEnabled = EditingIsEnabled,
                Description = EditingDescription,
                LastSignalAt = _editingTarget.LastSignalAt,
                LastTradeType = _editingTarget.LastTradeType,
                LastSide = _editingTarget.LastSide,
                LastPrice = _editingTarget.LastPrice
            });
            StatusMessage = $"更新: {newName}/{newInterval}m";
            _logger.LogInformation(
                "Strategy updated: {OldName}/{OldInterval} → {NewName}/{NewInterval}",
                oldName, oldInterval, newName, newInterval);

            IsEditing = false;
            _editingTarget = null;
            ClearFields();
            ReloadEntries();
            SelectedEntry = Entries.FirstOrDefault(
                e => e.AlertName == newName && e.Interval == newInterval);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update strategy");
            StatusMessage = $"更新エラー: {ex.Message}";
        }
    }
    private bool CanUpdate() => IsEditing;

    /// <summary>キャンセル: 編集モード解除しフィールドクリア。</summary>
    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        IsEditing = false;
        _editingTarget = null;
        ClearFields();
        StatusMessage = "編集キャンセル";
    }
    private bool CanCancel() => IsEditing;

    /// <summary>削除: 選択行を削除。編集中は無効。</summary>
    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task Delete()
    {
        if (SelectedEntry is null) return;
        try
        {
            var name = SelectedEntry.AlertName;
            var interval = SelectedEntry.Interval;
            await _registry.RemoveAsync(name, interval);
            StatusMessage = $"削除: {name}/{interval}m";
            _logger.LogInformation("Strategy deleted: {Name}/{Interval}m", name, interval);
            ReloadEntries();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete strategy");
            StatusMessage = $"削除エラー: {ex.Message}";
        }
    }
    private bool CanDelete() => !IsEditing && SelectedEntry is not null;
}

public partial class StrategyEditorRow : ObservableObject
{
    [ObservableProperty] private string _alertName = string.Empty;
    [ObservableProperty] private int _interval;
    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private string _description = string.Empty;
    [ObservableProperty] private DateTime? _lastSignalAt;
    public string? LastTradeType { get; set; }
    public string? LastSide { get; set; }
    public decimal? LastPrice { get; set; }
}
