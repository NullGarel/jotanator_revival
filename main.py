import asyncio
import os
import shutil
import subprocess
import sys
import tempfile
import threading
from datetime import datetime
from pathlib import Path

import discord
import gi
from dotenv import load_dotenv

gi.require_version("Gtk", "4.0")
from gi.repository import GLib, Gtk, Pango  # noqa: E402

load_dotenv()

FALLBACK_VOICES = ["en-us", "en-gb", "fr", "de", "es", "it", "pt", "nl", "ru", "ja"]


def list_espeak_voices() -> list[str]:
    """Return language/voice codes known to espeak-ng (falls back to a short list)."""
    try:
        out = subprocess.run(
            ["espeak-ng", "--voices"], capture_output=True, text=True, timeout=10
        ).stdout.splitlines()[1:]
        voices = sorted({line.split()[1] for line in out if len(line.split()) > 1})
        return voices or FALLBACK_VOICES
    except (OSError, subprocess.SubprocessError):
        return FALLBACK_VOICES


def synthesize_ogg(text: str, voice: str, speed: int, pitch: int, out_path: str) -> None:
    """espeak-ng -> WAV (stdout) -> ffmpeg -> Opus in an .ogg container."""
    if not shutil.which("espeak-ng"):
        raise RuntimeError("espeak-ng not found in PATH")
    if not shutil.which("ffmpeg"):
        raise RuntimeError("ffmpeg not found in PATH (needed to encode .ogg)")

    wav = subprocess.run(
        ["espeak-ng", "--stdout", "-v", voice, "-s", str(speed), "-p", str(pitch), text],
        capture_output=True,
        check=True,
    ).stdout

    subprocess.run(
        ["ffmpeg", "-y", "-loglevel", "error", "-i", "pipe:0",
         "-c:a", "libopus", "-b:a", "48k", "-f", "ogg", out_path],
        input=wav,
        capture_output=True,
        check=True,
    )


def synthesize_wav(text: str, voice: str, speed: int, pitch: int, out_path: str) -> None:
    """espeak-ng -> WAV file (played into a voice channel through FFmpeg)."""
    if not shutil.which("espeak-ng"):
        raise RuntimeError("espeak-ng not found in PATH")
    if not shutil.which("ffmpeg"):
        raise RuntimeError("ffmpeg not found in PATH (needed for voice playback)")
    try:
        subprocess.run(
            ["espeak-ng", "-v", voice, "-s", str(speed), "-p", str(pitch), "-w", out_path, text],
            capture_output=True,
            check=True,
        )
    except subprocess.CalledProcessError as e:
        raise RuntimeError(f"espeak-ng failed: {(e.stderr or b'').decode(errors='ignore')[:200]}") from e


def on_ui_thread(fn) -> None:
    """Schedule fn on the GTK main loop (safe to call from any thread)."""
    def _run():
        fn()
        return GLib.SOURCE_REMOVE
    GLib.idle_add(_run)


def set_margins(widget: Gtk.Widget, size: int) -> None:
    widget.set_margin_top(size)
    widget.set_margin_bottom(size)
    widget.set_margin_start(size)
    widget.set_margin_end(size)


def make_textview(editable: bool = True) -> tuple[Gtk.ScrolledWindow, Gtk.TextView]:
    view = Gtk.TextView(wrap_mode=Gtk.WrapMode.WORD_CHAR, editable=editable)
    view.set_cursor_visible(editable)
    set_margins(view, 6)
    scrolled = Gtk.ScrolledWindow(hexpand=True, vexpand=True)
    scrolled.set_child(view)
    scrolled.add_css_class("frame")
    return scrolled, view


def buffer_text(buf: Gtk.TextBuffer) -> str:
    return buf.get_text(buf.get_start_iter(), buf.get_end_iter(), False).strip()


class BotWindow(Gtk.ApplicationWindow):
    def __init__(self, app: Gtk.Application) -> None:
        super().__init__(application=app, title="Discord Bot Controller")
        self.set_default_size(640, 740)

        # Background asyncio loop for discord.py
        self.loop = asyncio.new_event_loop()
        threading.Thread(target=self.loop.run_forever, daemon=True).start()
        self.client: discord.Client | None = None
        self.attachments: list[str] = []  # file paths queued for the next text message

        # Voice channel state
        self.vc: discord.VoiceClient | None = None
        self.in_voice = False
        self.vc_name = ""
        self.voice_channels: list[tuple[str, int]] = []  # (display name, channel id)
        self.text_channels: list[tuple[str, int]] = []
        self.tts_lock = asyncio.Lock()  # serializes speech so clips don't overlap

        self._build_ui()
        self.connect("close-request", self.on_close)

    # ---------- UI ----------
    def _build_ui(self) -> None:
        root = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=10)
        set_margins(root, 12)
        self.set_child(root)

        # Connection panel
        conn_frame = Gtk.Frame()
        grid = Gtk.Grid(column_spacing=10, row_spacing=8)
        set_margins(grid, 10)
        conn_frame.set_child(grid)
        root.append(conn_frame)

        grid.attach(Gtk.Label(label="Bot token", xalign=0), 0, 0, 1, 1)
        self.token_entry = Gtk.PasswordEntry(hexpand=True, show_peek_icon=True)
        self.token_entry.set_text(os.environ.get("DISCORD_BOT_TOKEN", ""))
        grid.attach(self.token_entry, 1, 0, 1, 1)

        grid.attach(Gtk.Label(label="Text channel", xalign=0), 0, 1, 1, 1)
        text_row = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=8)
        self.text_model = Gtk.StringList()
        self.text_dd = Gtk.DropDown(model=self.text_model, hexpand=True)
        self.text_dd.set_enable_search(True)
        self.text_dd.set_expression(Gtk.PropertyExpression.new(Gtk.StringObject, None, "string"))
        text_row.append(self.text_dd)
        self.text_refresh_btn = Gtk.Button(icon_name="view-refresh-symbolic")
        self.text_refresh_btn.set_tooltip_text("Reload channel lists")
        self.text_refresh_btn.connect("clicked", self.refresh_channels)
        text_row.append(self.text_refresh_btn)
        grid.attach(text_row, 1, 1, 1, 1)

        grid.attach(Gtk.Label(label="Reply to msg ID", xalign=0), 0, 2, 1, 1)
        self.reply_entry = Gtk.Entry(hexpand=True)
        self.reply_entry.set_placeholder_text("Optional - leave empty for a normal message")
        grid.attach(self.reply_entry, 1, 2, 1, 1)

        self.connect_btn = Gtk.Button(label="Connect")
        self.connect_btn.add_css_class("suggested-action")
        self.connect_btn.connect("clicked", self.toggle_connection)
        grid.attach(self.connect_btn, 2, 0, 1, 3)

        self.status = Gtk.Label(xalign=0)
        self._render_status(False)
        root.append(self.status)

        # Tabs: text / voice
        notebook = Gtk.Notebook(vexpand=True)
        notebook.append_page(self._build_text_tab(), Gtk.Label(label="Text message"))
        notebook.append_page(self._build_voice_tab(), Gtk.Label(label="Voice (.ogg)"))
        root.append(notebook)

        # Log
        log_scrolled, self.log_view = make_textview(editable=False)
        log_scrolled.set_vexpand(False)
        log_scrolled.set_min_content_height(110)
        self.log_buf = self.log_view.get_buffer()
        root.append(log_scrolled)

    def _build_text_tab(self) -> Gtk.Widget:
        box = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=8)
        set_margins(box, 8)

        scrolled, self.text_view = make_textview()
        self.text_buf = self.text_view.get_buffer()
        box.append(scrolled)

        attach_bar = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=8)
        attach_btn = Gtk.Button(label="📎 Attach files…")
        attach_btn.connect("clicked", self.pick_files)
        attach_bar.append(attach_btn)

        self.attach_label = Gtk.Label(
            label="No files attached", xalign=0, hexpand=True,
            ellipsize=Pango.EllipsizeMode.END,
        )
        self.attach_label.add_css_class("dim-label")
        attach_bar.append(self.attach_label)

        clear_btn = Gtk.Button(label="Clear")
        clear_btn.connect("clicked", self.clear_attachments)
        attach_bar.append(clear_btn)
        box.append(attach_bar)

        self.send_text_btn = Gtk.Button(label="Send message", halign=Gtk.Align.END)
        self.send_text_btn.add_css_class("suggested-action")
        self.send_text_btn.set_sensitive(False)
        self.send_text_btn.connect("clicked", self.send_text)
        box.append(self.send_text_btn)
        return box

    def _build_voice_tab(self) -> Gtk.Widget:
        box = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=8)
        set_margins(box, 8)

        # Voice channel picker
        vc_frame = Gtk.Frame(label="Voice channel")
        vc_inner = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=6)
        set_margins(vc_inner, 8)
        vc_frame.set_child(vc_inner)

        vc_row = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=8)
        self.vc_model = Gtk.StringList()
        self.vc_dd = Gtk.DropDown(model=self.vc_model, hexpand=True)
        self.vc_dd.set_enable_search(True)
        self.vc_dd.set_expression(Gtk.PropertyExpression.new(Gtk.StringObject, None, "string"))
        vc_row.append(self.vc_dd)

        self.vc_refresh_btn = Gtk.Button(label="Refresh")
        self.vc_refresh_btn.connect("clicked", self.refresh_channels)
        vc_row.append(self.vc_refresh_btn)

        self.join_btn = Gtk.Button(label="Join voice")
        self.join_btn.connect("clicked", self.toggle_voice)
        vc_row.append(self.join_btn)
        vc_inner.append(vc_row)

        self.vc_status = Gtk.Label(xalign=0)
        self.vc_status.add_css_class("dim-label")
        vc_inner.append(self.vc_status)
        box.append(vc_frame)

        scrolled, self.tts_view = make_textview()
        self.tts_buf = self.tts_view.get_buffer()
        box.append(scrolled)

        opts = Gtk.Grid(column_spacing=10, row_spacing=6)
        box.append(opts)

        voices = list_espeak_voices()
        self.voice_dd = Gtk.DropDown(model=Gtk.StringList.new(voices), hexpand=True)
        self.voice_dd.set_enable_search(True)
        self.voice_dd.set_expression(Gtk.PropertyExpression.new(Gtk.StringObject, None, "string"))
        if "en-us" in voices:
            self.voice_dd.set_selected(voices.index("en-us"))
        opts.attach(Gtk.Label(label="TTS voice", xalign=0), 0, 0, 1, 1)
        opts.attach(self.voice_dd, 1, 0, 1, 1)

        self.speed = Gtk.Scale.new_with_range(Gtk.Orientation.HORIZONTAL, 80, 450, 1)
        self.speed.set_value(160)
        self.speed.set_digits(0)
        self.speed.set_draw_value(True)
        self.speed.set_hexpand(True)
        opts.attach(Gtk.Label(label="Speed", xalign=0), 0, 1, 1, 1)
        opts.attach(self.speed, 1, 1, 1, 1)

        self.pitch = Gtk.Scale.new_with_range(Gtk.Orientation.HORIZONTAL, 0, 99, 1)
        self.pitch.set_value(50)
        self.pitch.set_digits(0)
        self.pitch.set_draw_value(True)
        self.pitch.set_hexpand(True)
        opts.attach(Gtk.Label(label="Pitch", xalign=0), 0, 2, 1, 1)
        opts.attach(self.pitch, 1, 2, 1, 1)

        actions = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=8, halign=Gtk.Align.END)

        self.send_voice_btn = Gtk.Button(label="Send as .ogg to text channel")
        self.send_voice_btn.set_sensitive(False)
        self.send_voice_btn.connect("clicked", self.send_voice)
        actions.append(self.send_voice_btn)

        self.stop_btn = Gtk.Button(label="Stop")
        self.stop_btn.connect("clicked", self.stop_speaking)
        actions.append(self.stop_btn)

        self.speak_btn = Gtk.Button(label="🔊 Speak in voice channel")
        self.speak_btn.add_css_class("suggested-action")
        self.speak_btn.connect("clicked", self.speak_in_voice)
        actions.append(self.speak_btn)
        box.append(actions)

        self._update_voice_ui()
        return box

    # ---------- helpers ----------
    def log_msg(self, msg: str) -> None:
        line = f"[{datetime.now():%H:%M:%S}] {msg}\n"

        def _write() -> None:
            self.log_buf.insert(self.log_buf.get_end_iter(), line)
            mark = self.log_buf.create_mark(None, self.log_buf.get_end_iter(), False)
            self.log_view.scroll_to_mark(mark, 0.0, False, 0.0, 0.0)
            self.log_buf.delete_mark(mark)

        on_ui_thread(_write)

    def _render_status(self, connected: bool) -> None:
        if connected:
            self.status.set_markup('<span foreground="#2fa84f">●</span> Connected')
        else:
            self.status.set_markup('<span foreground="gray">●</span> Disconnected')

    def set_connected(self, connected: bool) -> None:
        def _update() -> None:
            self.send_text_btn.set_sensitive(connected)
            self.send_voice_btn.set_sensitive(connected)
            self.connect_btn.set_label("Disconnect" if connected else "Connect")
            self.connect_btn.set_sensitive(True)
            self._render_status(connected)
            self._update_voice_ui()

        on_ui_thread(_update)

    def run_async(self, coro, on_error_prefix: str) -> None:
        fut = asyncio.run_coroutine_threadsafe(coro, self.loop)

        def _done(f) -> None:
            exc = f.exception()
            if exc:
                self.log_msg(f"{on_error_prefix}: {exc}")

        fut.add_done_callback(_done)

    def get_channel_id(self) -> int | None:
        idx = self.text_dd.get_selected()
        if idx == Gtk.INVALID_LIST_POSITION or idx >= len(self.text_channels):
            self.log_msg("Select a text channel first (connect the bot, then press Refresh).")
            return None
        return self.text_channels[idx][1]

    def get_reply_id(self) -> tuple[bool, int | None]:
        """Returns (ok, id). Empty field -> (True, None); malformed -> (False, None)."""
        raw = self.reply_entry.get_text().strip()
        if not raw:
            return True, None
        try:
            value = int(raw)
            if value <= 0:
                raise ValueError
            return True, value
        except ValueError:
            self.log_msg("Invalid message ID (must be a number). Nothing was sent.")
            return False, None

    @staticmethod
    def make_reference(channel_id: int, reply_id: int | None) -> discord.MessageReference | None:
        if reply_id is None:
            return None
        return discord.MessageReference(
            message_id=reply_id, channel_id=channel_id, fail_if_not_exists=True
        )

    # ---------- connection ----------
    def toggle_connection(self, _btn: Gtk.Button) -> None:
        self.connect_btn.set_sensitive(False)
        if self.client is None:
            token = self.token_entry.get_text().strip()
            if not token:
                self.log_msg("Please enter a bot token.")
                self.connect_btn.set_sensitive(True)
                return
            self.run_async(self._connect(token), "Connection failed")
        else:
            self.run_async(self._disconnect(), "Disconnect failed")

    async def _connect(self, token: str) -> None:
        # Voice needs a real gateway connection (not just HTTP) plus these two
        # non-privileged intents so the bot can see servers and voice channels.
        client = discord.Client(intents=discord.Intents(guilds=True, voice_states=True))
        try:
            await client.login(token)
            runner = asyncio.ensure_future(client.connect(reconnect=True))
            ready = asyncio.ensure_future(client.wait_until_ready())
            done, _ = await asyncio.wait({runner, ready}, return_when=asyncio.FIRST_COMPLETED)
            if runner in done:
                ready.cancel()
                runner.result()  # re-raises the gateway error, if any
                raise RuntimeError("Gateway connection closed unexpectedly")
        except Exception:
            await client.close()
            self.set_connected(False)
            raise
        self.client = client
        self.set_connected(True)
        self.log_msg(f"Logged in as {client.user}")
        await self._refresh_channels()

    async def _disconnect(self) -> None:
        if self.client:
            await self.client.close()  # also leaves any voice channel
            self.client = None
        self.vc = None
        self.in_voice = False
        on_ui_thread(lambda: self._set_channel_lists([], []))
        self.set_connected(False)
        self.log_msg("Disconnected.")

    # ---------- voice channels ----------
    @staticmethod
    def _fill_dropdown(
        dd: Gtk.DropDown,
        model: Gtk.StringList,
        old_entries: list[tuple[str, int]],
        new_entries: list[tuple[str, int]],
        preferred_id: int | None = None,
    ) -> None:
        """Replace a dropdown's items while keeping the currently selected channel."""
        selected_id = None
        idx = dd.get_selected()
        if idx != Gtk.INVALID_LIST_POSITION and idx < len(old_entries):
            selected_id = old_entries[idx][1]
        model.splice(0, model.get_n_items(), [name for name, _ in new_entries])
        target = selected_id if selected_id is not None else preferred_id
        for i, (_, channel_id) in enumerate(new_entries):
            if channel_id == target:
                dd.set_selected(i)
                break

    def _set_channel_lists(
        self, text_entries: list[tuple[str, int]], voice_entries: list[tuple[str, int]]
    ) -> None:
        # DISCORD_CHANNEL_ID from .env, if set, is preselected the first time
        env_id = os.environ.get("DISCORD_CHANNEL_ID", "").strip()
        preferred = int(env_id) if env_id.isdigit() else None
        self._fill_dropdown(self.text_dd, self.text_model, self.text_channels, text_entries, preferred)
        self.text_channels = text_entries
        self._fill_dropdown(self.vc_dd, self.vc_model, self.voice_channels, voice_entries)
        self.voice_channels = voice_entries

    def _update_voice_ui(self) -> None:
        connected = self.client is not None
        self.vc_dd.set_sensitive(connected and not self.in_voice)
        self.vc_refresh_btn.set_sensitive(connected and not self.in_voice)
        self.join_btn.set_sensitive(connected)
        self.join_btn.set_label("Leave voice" if self.in_voice else "Join voice")
        self.speak_btn.set_sensitive(self.in_voice)
        self.stop_btn.set_sensitive(self.in_voice)
        if self.in_voice:
            self.vc_status.set_text(f"🔊 In voice channel: {self.vc_name}")
        elif connected:
            self.vc_status.set_text("Not in a voice channel")
        else:
            self.vc_status.set_text("Connect the bot first")

    def refresh_channels(self, _btn: Gtk.Button | None = None) -> None:
        if self.client is None:
            return
        self.run_async(self._refresh_channels(), "Refresh failed")

    async def _refresh_channels(self) -> None:
        text_entries: list[tuple[str, int]] = []
        voice_entries: list[tuple[str, int]] = []
        for guild in sorted(self.client.guilds, key=lambda g: g.name.lower()):
            me = guild.me
            for ch in guild.text_channels:
                # Only list channels the bot can actually post in
                if me is not None:
                    perms = ch.permissions_for(me)
                    if not (perms.view_channel and perms.send_messages):
                        continue
                text_entries.append((f"{guild.name} › #{ch.name}", ch.id))
            for ch in guild.voice_channels:
                voice_entries.append((f"{guild.name} › {ch.name}", ch.id))
        on_ui_thread(lambda: self._set_channel_lists(text_entries, voice_entries))
        self.log_msg(
            f"Found {len(text_entries)} text and {len(voice_entries)} voice channel(s) "
            f"in {len(self.client.guilds)} server(s)."
        )

    def toggle_voice(self, _btn: Gtk.Button) -> None:
        if self.in_voice:
            self.join_btn.set_sensitive(False)
            self.run_async(self._leave_voice(), "Leave failed")
            return
        idx = self.vc_dd.get_selected()
        if idx == Gtk.INVALID_LIST_POSITION or idx >= len(self.voice_channels):
            self.log_msg("Select a voice channel first (press Refresh if the list is empty).")
            return
        name, channel_id = self.voice_channels[idx]
        self.join_btn.set_sensitive(False)
        self.run_async(self._join_voice(channel_id, name), "Join failed")

    async def _join_voice(self, channel_id: int, name: str) -> None:
        try:
            try:
                import nacl  # noqa: F401
            except ImportError:
                raise RuntimeError("PyNaCl missing - run: pip install -U 'discord.py[voice]'")
            channel = self.client.get_channel(channel_id)
            if channel is None:
                raise RuntimeError("Channel not found (press Refresh)")
            self.vc = await channel.connect()
            self.in_voice = True
            self.vc_name = name
            self.log_msg(f"Joined voice channel: {name}")
        finally:
            on_ui_thread(self._update_voice_ui)

    async def _leave_voice(self) -> None:
        try:
            if self.vc:
                await self.vc.disconnect()
            self.log_msg("Left voice channel.")
        finally:
            self.vc = None
            self.in_voice = False
            on_ui_thread(self._update_voice_ui)

    def speak_in_voice(self, _btn: Gtk.Button) -> None:
        text = buffer_text(self.tts_buf)
        if not text:
            return
        item = self.voice_dd.get_selected_item()
        voice = item.get_string() if item else "en-us"
        speed, pitch = int(self.speed.get_value()), int(self.pitch.get_value())
        self.run_async(self._speak(text, voice, speed, pitch), "Speech failed")
        self.tts_buf.set_text("")

    def stop_speaking(self, _btn: Gtk.Button) -> None:
        vc = self.vc
        if vc:
            self.loop.call_soon_threadsafe(vc.stop)

    async def _speak(self, text: str, voice: str, speed: int, pitch: int) -> None:
        async with self.tts_lock:  # queued: clips play one after another
            vc = self.vc
            if vc is None or not vc.is_connected():
                self.vc, self.in_voice = None, False
                on_ui_thread(self._update_voice_ui)
                raise RuntimeError("Not connected to a voice channel anymore")

            tmp_dir = tempfile.mkdtemp(prefix="dcspeak_")
            path = os.path.join(tmp_dir, "speech.wav")
            try:
                loop = asyncio.get_running_loop()
                await loop.run_in_executor(None, synthesize_wav, text, voice, speed, pitch, path)

                finished: asyncio.Future = loop.create_future()

                def _after(err: Exception | None) -> None:  # runs on the player thread
                    loop.call_soon_threadsafe(
                        lambda: finished.done() or finished.set_result(err)
                    )

                vc.play(discord.FFmpegPCMAudio(path), after=_after)
                self.log_msg(f"Speaking: {text[:60]}")
                err = await finished
                if err:
                    raise err
            finally:
                shutil.rmtree(tmp_dir, ignore_errors=True)

    # ---------- attachments ----------
    def pick_files(self, _btn: Gtk.Button) -> None:
        dialog = Gtk.FileDialog(title="Select files to attach")
        dialog.open_multiple(self, None, self._on_files_chosen)

    def _on_files_chosen(self, dialog: Gtk.FileDialog, result) -> None:
        try:
            files = dialog.open_multiple_finish(result)
        except GLib.Error:
            return  # dialog dismissed
        for i in range(files.get_n_items()):
            path = files.get_item(i).get_path()
            if path and path not in self.attachments:
                self.attachments.append(path)
        if len(self.attachments) > 10:
            self.log_msg("Discord allows at most 10 files per message; keeping the first 10.")
            self.attachments = self.attachments[:10]
        self._refresh_attachment_label()

    def clear_attachments(self, _btn: Gtk.Button | None = None) -> None:
        self.attachments.clear()
        self._refresh_attachment_label()

    def _refresh_attachment_label(self) -> None:
        if not self.attachments:
            self.attach_label.set_text("No files attached")
            self.attach_label.add_css_class("dim-label")
            return
        names = ", ".join(Path(p).name for p in self.attachments)
        total_mb = sum(os.path.getsize(p) for p in self.attachments) / (1024 * 1024)
        self.attach_label.set_text(f"{len(self.attachments)} file(s) • {total_mb:.1f} MB: {names}")
        self.attach_label.remove_css_class("dim-label")

    # ---------- sending ----------
    def send_text(self, _btn: Gtk.Button) -> None:
        content = buffer_text(self.text_buf)
        files = list(self.attachments)
        channel_id = self.get_channel_id()
        if (not content and not files) or channel_id is None:
            return
        ok, reply_id = self.get_reply_id()
        if not ok:
            return
        self.run_async(self._send_text(channel_id, content, files, reply_id), "Send failed")
        self.text_buf.set_text("")
        self.clear_attachments()

    async def _send_text(
        self, channel_id: int, content: str, files: list[str], reply_id: int | None
    ) -> None:
        channel = self.client.get_partial_messageable(channel_id)
        discord_files = [discord.File(p, filename=Path(p).name) for p in files]
        try:
            await channel.send(
                content or None,
                files=discord_files or None,
                reference=self.make_reference(channel_id, reply_id),
            )
        finally:
            for f in discord_files:
                f.close()
        summary = content[:60] if content else "(no text)"
        if files:
            summary += f" + {len(files)} attachment(s)"
        if reply_id:
            summary += f" (reply to {reply_id})"
        self.log_msg(f"Sent: {summary}")

    def send_voice(self, _btn: Gtk.Button) -> None:
        text = buffer_text(self.tts_buf)
        channel_id = self.get_channel_id()
        if not text or channel_id is None:
            return
        ok, reply_id = self.get_reply_id()
        if not ok:
            return
        item = self.voice_dd.get_selected_item()
        voice = item.get_string() if item else "en-us"
        speed, pitch = int(self.speed.get_value()), int(self.pitch.get_value())
        self.send_voice_btn.set_sensitive(False)
        threading.Thread(
            target=self._voice_worker,
            args=(channel_id, text, voice, speed, pitch, reply_id),
            daemon=True,
        ).start()

    def _voice_worker(
        self, channel_id: int, text: str, voice: str, speed: int, pitch: int, reply_id: int | None
    ) -> None:
        # Runs in its own thread so the UI doesn't freeze during synthesis
        tmp_dir = tempfile.mkdtemp(prefix="dcvoice_")
        path = os.path.join(tmp_dir, "voice.ogg")
        try:
            self.log_msg("Synthesizing speech...")
            synthesize_ogg(text, voice, speed, pitch, path)
            fut = asyncio.run_coroutine_threadsafe(
                self._send_file(channel_id, path, reply_id), self.loop
            )
            fut.result(timeout=60)
            self.log_msg("Voice message sent.")
            on_ui_thread(lambda: self.tts_buf.set_text(""))
        except subprocess.CalledProcessError as e:
            self.log_msg(f"TTS error: {(e.stderr or b'').decode(errors='ignore')[:200]}")
        except Exception as e:
            self.log_msg(f"Voice send failed: {e}")
        finally:
            shutil.rmtree(tmp_dir, ignore_errors=True)
            on_ui_thread(lambda: self.send_voice_btn.set_sensitive(self.client is not None))

    async def _send_file(self, channel_id: int, path: str, reply_id: int | None) -> None:
        channel = self.client.get_partial_messageable(channel_id)
        with open(path, "rb") as fp:
            await channel.send(
                file=discord.File(fp, filename="voice.ogg"),
                reference=self.make_reference(channel_id, reply_id),
            )

    # ---------- shutdown ----------
    def on_close(self, _win: Gtk.Window) -> bool:
        if self.client:
            try:
                asyncio.run_coroutine_threadsafe(self.client.close(), self.loop).result(timeout=5)
            except Exception:
                pass
        self.loop.call_soon_threadsafe(self.loop.stop)
        return False  # allow the window to close


class BotApp(Gtk.Application):
    def __init__(self) -> None:
        super().__init__(application_id="dev.jotanator.DiscordBotUI")

    def do_activate(self) -> None:
        win = self.props.active_window or BotWindow(self)
        win.present()


if __name__ == "__main__":
    sys.exit(BotApp().run(sys.argv))