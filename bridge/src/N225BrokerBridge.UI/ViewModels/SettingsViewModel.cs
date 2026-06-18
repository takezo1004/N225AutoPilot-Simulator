using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using N225BrokerBridge.UI.Services;

namespace N225BrokerBridge.UI.ViewModels;

/// <summary>
/// 設定ダイアログのビューモデル。
/// appsettings.Local.json から読み込み、編集して保存する。
/// 保存値はアプリ再起動で反映 (Singleton Options は起動時に構築されるため)。
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly LocalSettingsStore _store;
    private readonly ILogger<SettingsViewModel> _logger;

    [ObservableProperty] private string? _webhookPassphrase;
    [ObservableProperty] private int _webhookPort = 8000;

    /// <summary>
    /// kabu 接続環境。"Production" (port 18080、実発注) または "Verification" (port 18081、モック応答)。
    /// 切替には kabu Station 側で両環境の API パスワードを別々に設定しておく必要あり。
    /// </summary>
    [ObservableProperty] private string _kabuEnvironment = KabuEnvironments.Production;

    [ObservableProperty] private string? _kabuApiPassword;        // 本番用 (18080)
    [ObservableProperty] private string? _kabuApiPasswordTest;    // 検証用 (18081)
    [ObservableProperty] private string? _kabuOrderPassword;      // 取引暗証番号 (本番/検証 共通)

    /// <summary>
    /// 手動操作 (UI の買/売/返済/キャンセルボタン) 前に確認ダイアログを表示するか。
    /// デフォルトは true (誤発注防止優先)。バグ収束後は false にして高速操作モードへ。
    /// MainViewModel は手動発注メソッド実行のたびに LocalSettingsStore.Load() を呼ぶため、
    /// このトグルは再起動不要で即反映される (機密項目と異なり Options 注入していない)。
    ///
    /// ※ TradingView Webhook 経由の自動発注には影響しない (SignalHandler は MainViewModel を
    ///   経由せず直接 UseCase を呼ぶため、このフラグの読み取り箇所が存在しない)。
    /// </summary>
    [ObservableProperty] private bool _requireConfirmBeforeOrder = true;

    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _wasSaved;

    /// <summary>
    /// パスワード欄の平文表示トグル。
    /// true にすると 4 つの PasswordBox が一時的に TextBox に切り替わり、保存前の確認が可能。
    /// ダイアログを閉じると次回は false (マスク) で開く (永続化しない)。
    /// </summary>
    [ObservableProperty] private bool _showPasswordsInClear;

    public Visibility MaskedVisibility => ShowPasswordsInClear ? Visibility.Collapsed : Visibility.Visible;
    public Visibility ClearVisibility => ShowPasswordsInClear ? Visibility.Visible : Visibility.Collapsed;

    partial void OnShowPasswordsInClearChanged(bool value)
    {
        OnPropertyChanged(nameof(MaskedVisibility));
        OnPropertyChanged(nameof(ClearVisibility));
    }

    /// <summary>環境ラジオボタンバインド用 (UI から RadioButton で切替)。</summary>
    public bool IsProduction
    {
        get => KabuEnvironment == KabuEnvironments.Production;
        set { if (value) KabuEnvironment = KabuEnvironments.Production; OnPropertyChanged(nameof(IsVerification)); }
    }

    public bool IsVerification
    {
        get => KabuEnvironment == KabuEnvironments.Verification;
        set { if (value) KabuEnvironment = KabuEnvironments.Verification; OnPropertyChanged(nameof(IsProduction)); }
    }

    /// <summary>UI 補足表示用: 現在選択中の環境で接続される URL。</summary>
    public string ActiveBaseUrlPreview => KabuEnvironments.BaseUrlFor(KabuEnvironment);

    partial void OnKabuEnvironmentChanged(string value)
    {
        OnPropertyChanged(nameof(IsProduction));
        OnPropertyChanged(nameof(IsVerification));
        OnPropertyChanged(nameof(ActiveBaseUrlPreview));
    }

    public string SettingsFilePath => _store.FilePath;

    public SettingsViewModel(LocalSettingsStore store, ILogger<SettingsViewModel> logger)
    {
        _store = store;
        _logger = logger;
        Load();
    }

    private void Load()
    {
        var values = _store.Load();
        WebhookPassphrase = values.WebhookPassphrase;
        if (values.WebhookPort is int p) WebhookPort = p;
        KabuEnvironment = values.KabuEnvironment ?? KabuEnvironments.Production;
        KabuApiPassword = values.KabuApiPassword;
        KabuApiPasswordTest = values.KabuApiPasswordTest;
        KabuOrderPassword = values.KabuOrderPassword;
        RequireConfirmBeforeOrder = values.RequireConfirmBeforeOrder;
        StatusMessage = _store.Exists ? "既存設定を読み込みました" : "新規 (まだ保存されていません)";
    }

    /// <summary>
    /// webhook パスフレーズを暗号学的乱数で 16 文字の英数字として生成する
    /// (ローカル版ダッシュボードと同仕様: 英大小 + 数字、記号なし)。
    /// </summary>
    public static string GenerateAlphanumericPassphrase(int length = 16)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        return System.Security.Cryptography.RandomNumberGenerator.GetString(alphabet, length);
    }

    [RelayCommand]
    private void Save()
    {
        try
        {
            _store.Save(new LocalSettingsValues
            {
                WebhookPassphrase = WebhookPassphrase,
                WebhookPort = WebhookPort,
                KabuEnvironment = KabuEnvironment,
                KabuApiPassword = KabuApiPassword,
                KabuApiPasswordTest = KabuApiPasswordTest,
                KabuOrderPassword = KabuOrderPassword,
                RequireConfirmBeforeOrder = RequireConfirmBeforeOrder
            });
            StatusMessage = $"保存しました ({_store.FilePath})。設定は次回起動時に反映されます。";
            WasSaved = true;
            _logger.LogInformation(
                "Settings saved to {Path} (Kabu env={Env})",
                _store.FilePath, KabuEnvironment);
        }
        catch (Exception ex)
        {
            StatusMessage = $"保存エラー: {ex.Message}";
            _logger.LogError(ex, "Settings save failed");
        }
    }
}
