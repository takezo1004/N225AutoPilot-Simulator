"""
N225 Simulator Test Dashboard
==============================
N225BrokerBridge の --simulator モードを起動し、Webhook ペイロード 7 種を
ボタン 1 つで発火して動作確認できる Tkinter ダッシュボード。

ユースケース:
- 配布物の動作デモ (購読者が clone 直後に動かせる)
- 開発者の回帰テスト
- ブログ第 2 話の素材撮影

仕組み:
- [Start Bridge] でシミュレータ設定 (passphrase=abcdefg + TestStrategy 登録) を
  %LOCALAPPDATA%\\N225BrokerBridge\\*.simulator.json に書き出して N225BrokerBridge.UI.exe --simulator を起動
- 7 ボタンで docs/webhook_test/payloads/*.json を POST
- レスポンスは「レスポンスログ」に表示
- Bridge の log file を 1 秒間隔で tail して「Bridge ログ」に表示

依存: Python 3.10+ 標準ライブラリのみ (tkinter / urllib / subprocess / json / threading)
"""

from __future__ import annotations

import json
import os
import subprocess
import sys
import threading
import time
import tkinter as tk
import urllib.error
import urllib.request
from datetime import datetime
from pathlib import Path
from tkinter import scrolledtext


# ─── 定数 ─────────────────────────────────────────────
SCRIPT_DIR = Path(__file__).resolve().parent
WEBHOOK_URL = "http://localhost:8000/webhook/"


def _resolve_bridge_exe() -> Path:
    """Bridge exe を開発環境と配布環境の両方で見つける。
    開発環境 (リポ直下): N225BrokerBridge/src/N225BrokerBridge.UI/bin/Debug/net8.0-windows/N225BrokerBridge.UI.exe
    配布環境 (public/dashboard): ../bridge/src/N225BrokerBridge.UI/bin/Debug/net8.0-windows/N225BrokerBridge.UI.exe
    """
    candidates = [
        SCRIPT_DIR / "N225BrokerBridge" / "src" / "N225BrokerBridge.UI" / "bin" / "Debug"
            / "net8.0-windows" / "N225BrokerBridge.UI.exe",
        # フラット配布（dashboard＝パッケージ直下・bridge＝同階層 bridge/）
        SCRIPT_DIR / "bridge" / "src" / "N225BrokerBridge.UI" / "bin" / "Debug"
            / "net8.0-windows" / "N225BrokerBridge.UI.exe",
        SCRIPT_DIR.parent / "bridge" / "src" / "N225BrokerBridge.UI" / "bin" / "Debug"
            / "net8.0-windows" / "N225BrokerBridge.UI.exe",
        SCRIPT_DIR / "N225BrokerBridge" / "src" / "N225BrokerBridge.UI" / "bin" / "Release"
            / "net8.0-windows" / "N225BrokerBridge.UI.exe",
        SCRIPT_DIR / "bridge" / "src" / "N225BrokerBridge.UI" / "bin" / "Release"
            / "net8.0-windows" / "N225BrokerBridge.UI.exe",
        SCRIPT_DIR.parent / "bridge" / "src" / "N225BrokerBridge.UI" / "bin" / "Release"
            / "net8.0-windows" / "N225BrokerBridge.UI.exe",
    ]
    for c in candidates:
        if c.exists():
            return c
    return candidates[0]  # 見つからなくても最初の候補を返す (エラー文言で使う)


def _resolve_payload_dir() -> Path:
    """ペイロードフォルダを開発環境と配布環境の両方で見つける。
    開発環境 (リポ直下): docs/webhook_test/payloads/
    配布環境 (public/dashboard): webhook_test/payloads/  (sync が docs/webhook_test → webhook_test に展開)
    """
    candidates = [
        SCRIPT_DIR / "docs" / "webhook_test" / "payloads",
        SCRIPT_DIR / "webhook_test" / "payloads",
    ]
    for c in candidates:
        if c.exists():
            return c
    return candidates[0]


DEFAULT_BRIDGE_EXE = _resolve_bridge_exe()
DEFAULT_PAYLOAD_DIR = _resolve_payload_dir()

LOCAL_APP_DATA = Path(os.environ.get("LOCALAPPDATA") or (Path.home() / "AppData" / "Local"))
SIMULATOR_DIR = LOCAL_APP_DATA / "N225BrokerBridge"
SIMULATOR_SETTINGS = SIMULATOR_DIR / "appsettings.Local.simulator.json"
SIMULATOR_STRATEGIES = SIMULATOR_DIR / "strategies.simulator.json"
BRIDGE_LOG_DIR = SIMULATOR_DIR / "logs"

# Bridge 起動時に投入する設定
SIMULATOR_PASSPHRASE = "abcdefg"   # payloads と整合
SIMULATOR_PORT = 8000              # appsettings.json 既定値と整合
TEST_STRATEGY_NAME = "TestStrategy"
TEST_STRATEGY_INTERVAL = 5

# 7 ペイロード定義 (順序 = 画面上の並び)
# (タイトル, サブタイトル, ファイル名, 期待レスポンス本文サブ文字列, ボタン色)
PAYLOADS = [
    ("Test 1", "認証失敗 (passphrase 不一致)",     "01_auth_failed.json",          "Authenticated_Failed", "#B85450"),
    ("Test 2", "Bad JSON (パース失敗)",            "02_bad_json.txt",              "Bad Request",          "#B85450"),
    ("Test 3", "新規買い (flat → long)",            "03_new_buy.json",              "NewOrderDispatched_",  "#2E7D32"),
    ("Test 4", "返済 (long → flat)",                "04_exit_long.json",            "ExitOrderDispatched_", "#1565C0"),
    ("Test 5", "ドテン (short → long)",             "05_doten_short_to_long.json",  "DotenDispatched_",     "#6A1B9A"),
    ("Test 6", "未定義遷移 (flat → flat, Ignored)", "06_ignored_flat_to_flat.json", "Ignored_",             "#757575"),
    ("Test 7", "戦略未登録 (Ignored)",              "07_not_registered.json",       "Ignored_",             "#757575"),
]


# ─── シミュレータ設定書き出し ─────────────────────────────
def write_simulator_settings() -> tuple[Path, Path]:
    """Bridge 起動前に、--simulator が読む設定ファイル 2 つを生成する。
    既存ファイルがあれば上書きする (テスト都度クリーンな状態にする)。
    """
    SIMULATOR_DIR.mkdir(parents=True, exist_ok=True)

    # 1) appsettings.Local.simulator.json
    # 平文 passphrase で OK (LocalSettingsStore は enc: プレフィックスがない値も読める)
    settings = {
        "Webhook": {
            "Port": SIMULATOR_PORT,
            "Passphrase": SIMULATOR_PASSPHRASE,
        },
        "Behavior": {
            "RequireConfirmBeforeOrder": True,
        },
    }
    SIMULATOR_SETTINGS.write_text(
        json.dumps(settings, indent=2, ensure_ascii=False), encoding="utf-8"
    )

    # 2) strategies.simulator.json
    strategies = [
        {
            "alertName": TEST_STRATEGY_NAME,
            "interval": TEST_STRATEGY_INTERVAL,
            "isEnabled": True,
            "description": "Simulator test dashboard が自動登録した戦略",
        }
    ]
    SIMULATOR_STRATEGIES.write_text(
        json.dumps(strategies, indent=2, ensure_ascii=False), encoding="utf-8"
    )

    return SIMULATOR_SETTINGS, SIMULATOR_STRATEGIES


# ─── ダッシュボード本体 ─────────────────────────────────
class SimulatorTestDashboard:
    def __init__(self, root: tk.Tk):
        self.root = root
        self.root.title("N225 Simulator Test Dashboard")
        self.root.geometry("900x800")

        self.bridge_exe = DEFAULT_BRIDGE_EXE
        self.payload_dir = DEFAULT_PAYLOAD_DIR
        self.bridge_proc: subprocess.Popen | None = None
        self.log_tail_thread: threading.Thread | None = None
        self.log_tail_stop = threading.Event()
        self.current_log_file: Path | None = None
        self.log_file_pos = 0

        self._build_ui()
        self._refresh_bridge_status()

    # ── UI ────────────────────────────────────────────
    def _build_ui(self):
        # カラーパレット (ダークテーマ・全テキスト純白)
        BG_MAIN = "#1E1E1E"       # 全体背景 (濃灰)
        BG_HEADER = "#2A3340"     # ヘッダー (やや明るい濃灰)
        BG_PANEL = "#2D2D30"      # パネル背景
        BG_REMINDER = "#1E1E1E"   # リマインダも本体と同じ濃灰に統一 (色の対比で文字が黄ばんで見える問題回避)
        WHITE = "#FFFFFF"         # すべての文字色は純白で統一
        TEXT_PRIMARY = WHITE
        TEXT_SECONDARY = WHITE
        ACCENT = "#2E7D32"

        self.root.configure(bg=BG_MAIN)

        # ── ヘッダーバー (タイトル + Bridge 制御) ──
        header = tk.Frame(self.root, bg=BG_HEADER, height=60)
        header.pack(fill=tk.X)
        header.pack_propagate(False)

        tk.Label(header, text="N225 Simulator Test Dashboard",
                 fg=TEXT_PRIMARY, bg=BG_HEADER,
                 font=("Yu Gothic UI", 13, "bold")).pack(side=tk.LEFT, padx=16, pady=14)

        self.status_var = tk.StringVar(value="● Stopped")
        self.status_label = tk.Label(
            header, textvariable=self.status_var, fg=WHITE, bg=BG_HEADER,
            font=("Consolas", 10, "bold")
        )
        self.status_label.pack(side=tk.LEFT, padx=12)

        self.stop_btn = tk.Button(
            header, text="■ Stop Bridge", bg="#B85450", fg=TEXT_PRIMARY,
            font=("Yu Gothic UI", 9, "bold"), relief=tk.FLAT, padx=14, pady=4,
            cursor="hand2", state=tk.DISABLED,
            activebackground="#A04040", activeforeground=TEXT_PRIMARY,
            disabledforeground="#FFFFFF",
            command=self.on_stop_bridge
        )
        self.stop_btn.pack(side=tk.RIGHT, padx=8, pady=12)
        self.start_btn = tk.Button(
            header, text="▶ Start Bridge (--simulator)", bg=ACCENT, fg=TEXT_PRIMARY,
            font=("Yu Gothic UI", 9, "bold"), relief=tk.FLAT, padx=14, pady=4,
            cursor="hand2",
            activebackground="#1B5E20", activeforeground=TEXT_PRIMARY,
            disabledforeground="#FFFFFF",
            command=self.on_start_bridge
        )
        self.start_btn.pack(side=tk.RIGHT, padx=4, pady=12)

        # ── 操作手順リマインダ (2 段階) ──
        reminder = tk.Frame(self.root, bg=BG_REMINDER)
        reminder.pack(fill=tk.X)
        tk.Label(reminder,
                 text="📋 推奨テスト手順",
                 bg=BG_REMINDER, fg=WHITE,
                 font=("Yu Gothic UI", 9, "bold")).pack(padx=12, pady=(6, 0), anchor="w")
        tk.Label(reminder,
                 text="  ① Bridge 起動直後 (自動売買 OFF 状態) で Test 1〜7 を順に実行 → 全てが 自動売買 OFF 経路で動くことを確認",
                 bg=BG_REMINDER, fg=WHITE,
                 font=("Yu Gothic UI", 9)).pack(padx=12, pady=0, anchor="w")
        tk.Label(reminder,
                 text="  ② UI 右下の「自動売買」トグルを ON にしてから Test 1〜7 を再度実行 → 発注経路まで通ることを確認",
                 bg=BG_REMINDER, fg=WHITE,
                 font=("Yu Gothic UI", 9)).pack(padx=12, pady=(0, 6), anchor="w")

        # ── テストボタンエリア ──
        webhook_outer = tk.Frame(self.root, bg=BG_MAIN)
        webhook_outer.pack(fill=tk.X, padx=12, pady=(8, 4))

        tk.Label(webhook_outer, text="Webhook ペイロード発火",
                 bg=BG_MAIN, fg=TEXT_PRIMARY,
                 font=("Yu Gothic UI", 10, "bold")).pack(anchor="w", pady=(0, 4))

        webhook = tk.Frame(webhook_outer, bg=BG_MAIN)
        webhook.pack(fill=tk.X)

        self.payload_buttons: list[tk.Button] = []
        for i, (title, subtitle, _filename, _expect, color) in enumerate(PAYLOADS):
            row = i // 3
            col = i % 3
            cell = tk.Frame(webhook, bg=BG_MAIN)
            cell.grid(row=row, column=col, padx=4, pady=4, sticky="ew")
            btn = tk.Button(
                cell, text=f"{title}\n{subtitle}",
                bg=color, fg=TEXT_PRIMARY,
                font=("Yu Gothic UI", 9, "bold"),
                relief=tk.FLAT, justify=tk.CENTER, cursor="hand2",
                height=2, padx=8, pady=6, wraplength=240,
                activebackground=color, activeforeground=TEXT_PRIMARY,
                disabledforeground="#FFFFFF",
                state=tk.DISABLED,
                command=lambda idx=i: self.on_fire_payload(idx)
            )
            btn.pack(fill=tk.BOTH, expand=True)
            self.payload_buttons.append(btn)

        for col in range(3):
            webhook.grid_columnconfigure(col, weight=1, uniform="payload")

        # ── レスポンスログ ──
        resp_outer = tk.Frame(self.root, bg=BG_MAIN)
        resp_outer.pack(fill=tk.X, padx=12, pady=(8, 4))
        tk.Label(resp_outer, text="レスポンスログ",
                 bg=BG_MAIN, fg=TEXT_PRIMARY,
                 font=("Yu Gothic UI", 10, "bold")).pack(anchor="w", pady=(0, 4))
        self.response_log = scrolledtext.ScrolledText(
            resp_outer, height=9, font=("Consolas", 9), wrap=tk.NONE,
            background=BG_PANEL, foreground=TEXT_SECONDARY,
            insertbackground=TEXT_PRIMARY,
            borderwidth=1, relief=tk.SOLID
        )
        self.response_log.pack(fill=tk.X)
        self.response_log.tag_config("pass", foreground=WHITE, font=("Consolas", 9, "bold"))
        self.response_log.tag_config("fail", foreground=WHITE, font=("Consolas", 9, "bold"))
        self.response_log.tag_config("info", foreground=WHITE)

        # ── Bridge ログ tail ──
        bridge_outer = tk.Frame(self.root, bg=BG_MAIN)
        bridge_outer.pack(fill=tk.BOTH, expand=True, padx=12, pady=(8, 12))
        tk.Label(bridge_outer, text="Bridge ログ tail",
                 bg=BG_MAIN, fg=TEXT_PRIMARY,
                 font=("Yu Gothic UI", 10, "bold")).pack(anchor="w", pady=(0, 4))
        self.bridge_log = scrolledtext.ScrolledText(
            bridge_outer, height=14, font=("Consolas", 8), wrap=tk.NONE,
            background="#0F0F0F", foreground=WHITE,
            insertbackground=TEXT_PRIMARY,
            borderwidth=1, relief=tk.SOLID
        )
        self.bridge_log.pack(fill=tk.BOTH, expand=True)

        # ── 終了時のクリーンアップ ──
        self.root.protocol("WM_DELETE_WINDOW", self.on_close)

    # ── 状態反映 ───────────────────────────────────────
    def _refresh_bridge_status(self):
        running = self.bridge_proc is not None and self.bridge_proc.poll() is None
        if running:
            self.status_var.set(f"● Running (PID {self.bridge_proc.pid})  port {SIMULATOR_PORT}")
            self.status_label.config(fg="#FFFFFF")
            self.start_btn.config(state=tk.DISABLED)
            self.stop_btn.config(state=tk.NORMAL)
            for btn in self.payload_buttons:
                btn.config(state=tk.NORMAL)
        else:
            self.status_var.set("● Stopped")
            self.status_label.config(fg="#FFFFFF")
            self.start_btn.config(state=tk.NORMAL)
            self.stop_btn.config(state=tk.DISABLED)
            for btn in self.payload_buttons:
                btn.config(state=tk.DISABLED)
            if self.bridge_proc is not None and self.bridge_proc.poll() is not None:
                # プロセスが死んだ場合は参照クリア
                self.bridge_proc = None
        # 1 秒ごとに再評価
        self.root.after(1000, self._refresh_bridge_status)

    # ── Bridge 起動/停止 ──────────────────────────────────
    def on_start_bridge(self):
        if self.bridge_proc and self.bridge_proc.poll() is None:
            self._log_response("info", "Bridge は既に起動しています")
            return

        if not self.bridge_exe.exists():
            self._log_response("fail", f"Bridge exe が見つかりません: {self.bridge_exe}")
            return

        # 1) シミュレータ設定を書き出す (毎回上書き = テストの再現性確保)
        try:
            sp, sg = write_simulator_settings()
            self._log_response("info", f"設定書き出し OK: {sp.name} / {sg.name}")
        except Exception as e:
            self._log_response("fail", f"設定書き出し失敗: {e}")
            return

        # 2) Bridge を起動
        try:
            self.bridge_proc = subprocess.Popen(
                [str(self.bridge_exe), "--simulator"],
                creationflags=subprocess.CREATE_NEW_CONSOLE if os.name == "nt" else 0,
            )
            self._log_response("info", f"Bridge 起動 (PID {self.bridge_proc.pid})")
        except Exception as e:
            self._log_response("fail", f"Bridge 起動失敗: {e}")
            return

        # 3) ログ tail スレッド起動
        self.log_tail_stop.clear()
        self.log_tail_thread = threading.Thread(target=self._log_tail_loop, daemon=True)
        self.log_tail_thread.start()

    def on_stop_bridge(self):
        if self.bridge_proc and self.bridge_proc.poll() is None:
            try:
                self.bridge_proc.terminate()
                self.bridge_proc.wait(timeout=5)
                self._log_response("info", "Bridge 停止")
            except subprocess.TimeoutExpired:
                self.bridge_proc.kill()
                self._log_response("info", "Bridge 強制停止")
            except Exception as e:
                self._log_response("fail", f"Bridge 停止失敗: {e}")
        self.log_tail_stop.set()
        self.bridge_proc = None

    # ── ペイロード送信 ─────────────────────────────────
    def on_fire_payload(self, index: int):
        title, subtitle, filename, expect, _color = PAYLOADS[index]
        label = f"{title} ({subtitle})"
        path = self.payload_dir / filename
        if not path.exists():
            self._log_response("fail", f"ペイロード見つかりません: {path}")
            return
        body = path.read_bytes()

        # ボタン連打防止
        self.payload_buttons[index].config(state=tk.DISABLED)
        threading.Thread(target=self._send_payload_worker, args=(label, body, expect, index), daemon=True).start()

    def _send_payload_worker(self, label: str, body: bytes, expect: str, index: int):
        try:
            req = urllib.request.Request(
                WEBHOOK_URL,
                data=body,
                method="POST",
                headers={"Content-Type": "application/json; charset=utf-8"},
            )
            with urllib.request.urlopen(req, timeout=5) as resp:
                status = resp.status
                text = resp.read().decode("utf-8", errors="replace").strip()
        except urllib.error.HTTPError as e:
            status = e.code
            try:
                text = e.read().decode("utf-8", errors="replace").strip()
            except Exception:
                text = str(e)
        except Exception as e:
            self.root.after(0, self._log_response, "fail", f"{label} → 接続エラー: {e}")
            self.root.after(0, self._reenable_button, index)
            return

        ok = expect in text
        tag = "pass" if ok else "fail"
        mark = "PASS" if ok else "FAIL"
        self.root.after(0, self._log_response, tag, f"[{mark}] {label} → HTTP {status}  body={text}")
        self.root.after(0, self._reenable_button, index)

    def _reenable_button(self, index: int):
        running = self.bridge_proc is not None and self.bridge_proc.poll() is None
        if running:
            self.payload_buttons[index].config(state=tk.NORMAL)

    # ── ログ tail ────────────────────────────────────
    def _log_tail_loop(self):
        # 起動直後はログがまだ無いので少し待つ
        time.sleep(1.0)
        while not self.log_tail_stop.is_set():
            try:
                log_file = self._find_latest_log_file()
                if log_file is None:
                    time.sleep(1.0)
                    continue
                if log_file != self.current_log_file:
                    # 新しい日のログに切り替わったら最初から
                    self.current_log_file = log_file
                    self.log_file_pos = 0
                with open(log_file, "rb") as f:
                    f.seek(self.log_file_pos)
                    new_bytes = f.read()
                    self.log_file_pos = f.tell()
                if new_bytes:
                    text = new_bytes.decode("utf-8", errors="replace")
                    # MainThread に投げる
                    self.root.after(0, self._append_bridge_log, text)
            except Exception:
                pass
            time.sleep(1.0)

    def _find_latest_log_file(self) -> Path | None:
        if not BRIDGE_LOG_DIR.exists():
            return None
        candidates = sorted(BRIDGE_LOG_DIR.glob("n225brokerbridge-*.log"), key=lambda p: p.stat().st_mtime)
        return candidates[-1] if candidates else None

    def _append_bridge_log(self, text: str):
        self.bridge_log.insert(tk.END, text)
        # 行数が増えすぎたら古い行を削る (上限 1000 行)
        line_count = int(self.bridge_log.index("end-1c").split(".")[0])
        if line_count > 1000:
            self.bridge_log.delete("1.0", f"{line_count - 1000}.0")
        self.bridge_log.see(tk.END)

    # ── レスポンスログ ─────────────────────────────────
    def _log_response(self, tag: str, text: str):
        ts = datetime.now().strftime("%H:%M:%S")
        self.response_log.insert(tk.END, f"[{ts}] {text}\n", tag)
        self.response_log.see(tk.END)

    # ── 終了 ───────────────────────────────────────────
    def on_close(self):
        if self.bridge_proc and self.bridge_proc.poll() is None:
            self.on_stop_bridge()
        self.log_tail_stop.set()
        self.root.destroy()


def main():
    root = tk.Tk()

    # ── 全 widget のデフォルト色を強制 (Windows ダーク/ライトモード上書き) ──
    # 個別の widget で fg/bg を指定していても、Windows のシステムテーマで上書きされる
    # ケースがあるため、option_add で root レベルで強制する。
    root.option_add("*foreground", "#FFFFFF")
    root.option_add("*background", "#1E1E1E")
    root.option_add("*Label.foreground", "#FFFFFF")
    root.option_add("*Label.background", "#1E1E1E")
    root.option_add("*Frame.background", "#1E1E1E")
    root.option_add("*Text.foreground", "#FFFFFF")
    root.option_add("*Text.background", "#1E1E1E")
    root.option_add("*Button.foreground", "#FFFFFF")

    try:
        from tkinter import font as tkfont
        default_font = tkfont.nametofont("TkDefaultFont")
        default_font.configure(family="Yu Gothic UI", size=9)
    except Exception:
        pass
    SimulatorTestDashboard(root)
    root.mainloop()


if __name__ == "__main__":
    main()
