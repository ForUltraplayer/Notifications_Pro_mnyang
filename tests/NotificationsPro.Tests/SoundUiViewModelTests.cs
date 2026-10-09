// @file SoundUiViewModelTests.cs
// @author: rudals252
// @brief P2-B 소리 탭·앱 탭 ViewModel 테스트: 기본 규칙 편집 왕복(순서·모드), 설정창 저장 시 규칙 비유실, 정책·모드 저장과 undo/redo, 앱별 상속/무음/전용 전환, 여러 AUMID 키 선택, 가져오기 표시 이름 저장, 목록 제외와 실제 삭제 분리, 미리 듣기 순서 비소비, 진단 줄 내용, 폴더 추가 거부 사유, 탭 진입 스캔.

using System.IO;
using System.Reflection;
using NotificationsPro.Models;
using NotificationsPro.Services;
using NotificationsPro.ViewModels;

namespace NotificationsPro.Tests;

public class SoundUiViewModelTests : IDisposable
{
    private readonly string _root = TestAppDataIsolation.CreateUniqueDirectory("sound-ui");
    private readonly string _tag = Guid.NewGuid().ToString("N")[..8];

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private sealed class Harness
    {
        public required SettingsManager Manager { get; init; }
        public required QueueManager Queue { get; init; }
        public required SettingsViewModel ViewModel { get; init; }
        public required SoundLibraryService Library { get; init; }
        public required PlaybackCoordinator Coordinator { get; init; }
        public required FakeSoundEngine Engine { get; init; }
        public required ManualPostQueue Posts { get; init; }
    }

    private Harness Create(Action<AppSettings>? arrange = null)
    {
        var manager = new SettingsManager(Path.Combine(_root, "settings"));
        manager.Load();
        if (arrange != null)
        {
            var updated = manager.Settings.Clone();
            arrange(updated);
            manager.Apply(updated);
        }

        var queue = new QueueManager(manager);
        var viewModel = new SettingsViewModel(manager, queue, new ProfileManager(Path.Combine(_root, "profiles")));
        var library = new SoundLibraryService(() => manager.Settings);
        var engine = new FakeSoundEngine();
        var posts = new ManualPostQueue();
        var coordinator = new PlaybackCoordinator(manager, engine, library, posts.Post);
        viewModel.ConfigureSoundPlayback(library, coordinator);
        viewModel.ConfirmSoundAction = (_, _) => false;
        viewModel.OpenFolderAction = _ => { };
        return new Harness
        {
            Manager = manager,
            Queue = queue,
            ViewModel = viewModel,
            Library = library,
            Coordinator = coordinator,
            Engine = engine,
            Posts = posts,
        };
    }

    private string SourceWav(string name, byte fill, string? folder = null)
    {
        return TestWavFactory.Write(folder ?? Path.Combine(_root, "src"), name, TestWavFactory.Pcm(0.1, fill: fill));
    }

    /// <summary>Imports through the view model's file picker hook and returns the rule references.</summary>
    private static List<string> Import(Harness h, params string[] paths)
    {
        h.ViewModel.PickSoundFilesAction = () => paths;
        h.ViewModel.ImportSoundFilesCommand.Execute(null);
        return paths
            .Select(path => h.ViewModel.SoundLibraryItems.Single(item =>
                File.ReadAllBytes(item.FullPath).SequenceEqual(File.ReadAllBytes(path))).RuleReference)
            .ToList();
    }

    private static SoundLibraryItemViewModel Item(Harness h, string reference)
        => h.ViewModel.SoundLibraryItems.Single(item => string.Equals(item.RuleReference, reference, StringComparison.OrdinalIgnoreCase));

    private static void Save(SettingsViewModel viewModel) => InvokePrivate(viewModel, "SaveSettings");

    private static void InvokePrivate(SettingsViewModel viewModel, string methodName)
    {
        var method = typeof(SettingsViewModel).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(viewModel, null);
    }

    [Fact]
    public void DefaultRuleEdit_RoundTripsOrderModeAndFlags_ThroughSaveAndNewViewModel()
    {
        StaThreadTestHelper.Run(() =>
        {
            var h = Create();
            var refs = Import(h, SourceWav($"a-{_tag}.wav", 1), SourceWav($"b-{_tag}.wav", 2));

            // Check A then B: play order is check order, then B is moved up (not name order).
            Item(h, refs[0]).DefaultChoice!.IsSelected = true;
            Item(h, refs[1]).DefaultChoice!.IsSelected = true;
            var editor = h.ViewModel.DefaultRuleEditor;
            editor.MoveUpCommand.Execute(editor.Files.Single(f => f.RuleReference == refs[1]));
            editor.Mode = SoundSelectionMode.Random;
            editor.AvoidRepeat = true;
            h.ViewModel.DefaultRuleExplicitSilence = true;
            h.ViewModel.DefaultRuleEnabled = false;
            Save(h.ViewModel);

            var rule = h.Manager.Settings.DefaultSoundRule;
            Assert.Equal(new[] { refs[1], refs[0] }, rule.Files);
            Assert.Equal(SoundSelectionMode.Random, rule.Mode);
            Assert.True(rule.AvoidRepeat);
            Assert.True(rule.ExplicitSilence);
            Assert.False(rule.Enabled);

            var reloaded = new SettingsViewModel(h.Manager, new QueueManager(h.Manager), new ProfileManager(Path.Combine(_root, "profiles")));
            Assert.Equal(new[] { refs[1], refs[0] }, reloaded.DefaultRuleEditor.GetFiles());
            Assert.Equal(new[] { 1, 2 }, reloaded.DefaultRuleEditor.Files.Select(f => f.Position));
            Assert.Equal(SoundSelectionMode.Random, reloaded.DefaultRuleEditor.Mode);
            Assert.True(reloaded.DefaultRuleEditor.AvoidRepeat);
            Assert.False(reloaded.DefaultRuleEnabled);
            Assert.True(reloaded.DefaultRuleExplicitSilence);

            // Unchecking in the library removes it from the list.
            Item(h, refs[0]).DefaultChoice!.IsSelected = false;
            Save(h.ViewModel);
            Assert.Equal(new[] { refs[1] }, h.Manager.Settings.DefaultSoundRule.Files);
        });
    }

    [Fact]
    public void SettingsWindowSave_OfUnrelatedValue_DoesNotLoseRulesPolicyOrDisplayNames()
    {
        StaThreadTestHelper.Run(() =>
        {
            var h = Create(s =>
            {
                s.SoundEnabled = true;
                s.DefaultSoundRule = new SoundRule { Mode = SoundSelectionMode.Sequential, Files = { "sounds/x.wav", "sounds/y.wav" } };
                s.AppSoundRules["Discord"] = new SoundRule { ExplicitSilence = true };
                s.AppSoundRules["OpenAI.Codex!App"] = new SoundRule { Mode = SoundSelectionMode.Random, Files = { "sounds/z.wav" } };
                s.PlaybackPolicy = new PlaybackPolicy { MinIntervalPerAppSeconds = 7, MaxQueued = 2, MaxQueueWaitSeconds = 11, OverflowBehavior = PlaybackOverflowBehavior.Skip };
                s.AudioDispatchMode = AudioDispatchMode.OverlayAccepted;
                s.SoundDisplayNames["sounds/x.wav"] = "Chime";
            });

            h.ViewModel.FontSize = 21;
            Save(h.ViewModel);

            var s = h.Manager.Settings;
            Assert.Equal(21, s.FontSize);
            Assert.Equal(new[] { "sounds/x.wav", "sounds/y.wav" }, s.DefaultSoundRule.Files);
            Assert.Equal(SoundSelectionMode.Sequential, s.DefaultSoundRule.Mode);
            Assert.True(s.AppSoundRules["Discord"].ExplicitSilence);
            Assert.Equal(new[] { "sounds/z.wav" }, s.AppSoundRules["OpenAI.Codex!App"].Files);
            Assert.Equal(7, s.PlaybackPolicy.MinIntervalPerAppSeconds);
            Assert.Equal(2, s.PlaybackPolicy.MaxQueued);
            Assert.Equal(11, s.PlaybackPolicy.MaxQueueWaitSeconds);
            Assert.Equal(PlaybackOverflowBehavior.Skip, s.PlaybackPolicy.OverflowBehavior);
            Assert.Equal(AudioDispatchMode.OverlayAccepted, s.AudioDispatchMode);
            Assert.Equal("Chime", s.SoundDisplayNames["sounds/x.wav"]);
        });
    }

    [Fact]
    public void PolicyAndDispatchModeEdits_AreSaved_AndUndoRedoRestoreThem()
    {
        StaThreadTestHelper.Run(() =>
        {
            var h = Create();
            Assert.Equal(4, h.ViewModel.MinIntervalPerAppSeconds);
            Assert.Equal(AudioDispatchMode.PolicyAccepted, h.ViewModel.SoundAudioDispatchMode);

            h.ViewModel.MinIntervalPerAppSeconds = 0;
            h.ViewModel.MaxQueuedSounds = 3;
            h.ViewModel.MaxQueueWaitSeconds = 12;
            h.ViewModel.SoundOverflowBehavior = PlaybackOverflowBehavior.Skip;
            h.ViewModel.SoundAudioDispatchMode = AudioDispatchMode.OverlayAccepted;
            Assert.Equal("Off", h.ViewModel.MinIntervalPerAppText);
            Save(h.ViewModel);

            var policy = h.Manager.Settings.PlaybackPolicy;
            Assert.Equal(0, policy.MinIntervalPerAppSeconds);
            Assert.Equal(3, policy.MaxQueued);
            Assert.Equal(12, policy.MaxQueueWaitSeconds);
            Assert.Equal(PlaybackOverflowBehavior.Skip, policy.OverflowBehavior);
            Assert.Equal(AudioDispatchMode.OverlayAccepted, h.Manager.Settings.AudioDispatchMode);

            h.ViewModel.UndoCommand.Execute(null);
            Assert.Equal(4, h.Manager.Settings.PlaybackPolicy.MinIntervalPerAppSeconds);
            Assert.Equal(AudioDispatchMode.PolicyAccepted, h.Manager.Settings.AudioDispatchMode);
            Assert.Equal(4, h.ViewModel.MinIntervalPerAppSeconds);
            Assert.Equal(AudioDispatchMode.PolicyAccepted, h.ViewModel.SoundAudioDispatchMode);

            h.ViewModel.RedoCommand.Execute(null);
            Assert.Equal(0, h.Manager.Settings.PlaybackPolicy.MinIntervalPerAppSeconds);
            Assert.Equal(0, h.ViewModel.MinIntervalPerAppSeconds);
        });
    }

    [Fact]
    public void AppRule_SwitchesBetweenInheritSilenceAndCustom_AndSilenceNeverFallsBack()
    {
        StaThreadTestHelper.Run(() =>
        {
            var h = Create(s =>
            {
                s.SoundEnabled = true;
                s.DefaultSoundRule = new SoundRule { Files = { "sounds/default.wav" } };
                s.PerAppSounds["Discord"] = "None"; // original app's "None" (normalized away = inherit)
            });
            var refs = Import(h, SourceWav($"d-{_tag}.wav", 3));
            h.Queue.AddNotification("Discord", "title", "body");
            h.ViewModel.RefreshPerAppConfig();
            var app = h.ViewModel.PerAppConfigEntries.Single(e => e.AppName == "Discord").SoundRule!;
            Assert.Equal(AppSoundRuleChoice.Inherit, app.Choice);
            Assert.Equal("Accessibility (no app ID)", app.ReceivePathText);
            Assert.Equal("Not checked", app.OriginalSoundStatusText);

            app.Choice = AppSoundRuleChoice.Silence;
            var silent = h.Manager.Settings.AppSoundRules["Discord"];
            Assert.True(silent.ExplicitSilence);
            var resolution = new SoundRuleSelector().Resolve(h.Manager.Settings, "Discord", null);
            Assert.True(resolution.IsSilent);
            Assert.Equal(SoundSilenceReason.ExplicitSilence, resolution.SilenceReason);

            app.Choice = AppSoundRuleChoice.Custom;
            app.Editor.Choices.Single(c => c.RuleReference == refs[0]).IsSelected = true;
            app.Editor.Mode = SoundSelectionMode.Sequential;
            var custom = h.Manager.Settings.AppSoundRules["Discord"];
            Assert.False(custom.ExplicitSilence);
            Assert.True(custom.Enabled);
            Assert.Equal(SoundSelectionMode.Sequential, custom.Mode);
            Assert.Equal(new[] { refs[0] }, custom.Files);
            Assert.Contains("Own list", app.SummaryText);

            // A later settings-window save keeps the app rule (copied from current settings).
            h.ViewModel.FontSize = 19;
            Save(h.ViewModel);
            Assert.Equal(new[] { refs[0] }, h.Manager.Settings.AppSoundRules["Discord"].Files);

            app.Choice = AppSoundRuleChoice.Inherit;
            Assert.False(h.Manager.Settings.AppSoundRules.ContainsKey("Discord"));
            Assert.Equal(new[] { "sounds/default.wav" },
                new SoundRuleSelector().Resolve(h.Manager.Settings, "Discord", null).Rule!.Files);
        });
    }

    [Fact]
    public void AppRule_SeveralAumidsForOneName_UserChoosesKey_LegacyNameValuesUntouched()
    {
        StaThreadTestHelper.Run(() =>
        {
            var h = Create(s =>
            {
                s.SoundEnabled = true;
                s.PerAppIcons["Chrome"] = "Bell";
                s.MutedApps = new List<string>();
            });
            h.Queue.AddNotification("Chrome", "first", "one", "Chrome.Profile1");
            h.Queue.AddNotification("Chrome", "second", "two", "Chrome.Profile2");
            h.ViewModel.RefreshPerAppConfig();
            var app = h.ViewModel.PerAppConfigEntries.Single(e => e.AppName == "Chrome").SoundRule!;
            Assert.True(app.HasMultipleAppIds);
            Assert.Equal(new[] { "Chrome", "Chrome.Profile1", "Chrome.Profile2" }, app.RuleKeyOptions.Select(o => o.Value));
            Assert.StartsWith("WinRT (app ID known)", app.ReceivePathText);

            app.Choice = AppSoundRuleChoice.Silence;
            Assert.True(h.Manager.Settings.AppSoundRules["Chrome"].ExplicitSilence);

            app.SelectedRuleKey = "Chrome.Profile2";
            var rules = h.Manager.Settings.AppSoundRules;
            Assert.False(rules.ContainsKey("Chrome"));
            Assert.True(rules["Chrome.Profile2"].ExplicitSilence);
            Assert.Equal("Bell", h.Manager.Settings.PerAppIcons["Chrome"]);

            // Only Profile2 is silent now; Profile1 falls back to the default rule.
            var selector = new SoundRuleSelector();
            Assert.Equal(SoundSilenceReason.ExplicitSilence, selector.Resolve(h.Manager.Settings, "Chrome", "Chrome.Profile2").SilenceReason);
            Assert.NotEqual(SoundSilenceReason.ExplicitSilence, selector.Resolve(h.Manager.Settings, "Chrome", "Chrome.Profile1").SilenceReason);

            // Reloading keeps editing the AUMID-keyed rule.
            h.ViewModel.RefreshPerAppConfig();
            var reloaded = h.ViewModel.PerAppConfigEntries.Single(e => e.AppName == "Chrome").SoundRule!;
            Assert.Equal("Chrome.Profile2", reloaded.RuleKey);
            Assert.Equal(AppSoundRuleChoice.Silence, reloaded.Choice);
        });
    }

    [Fact]
    public void Import_NameCollision_GetsUniqueInternalName_AndDisplayNameIsSaved()
    {
        StaThreadTestHelper.Run(() =>
        {
            var h = Create();
            var name = $"chime-{_tag}.wav";
            var first = Import(h, SourceWav(name, 4, Path.Combine(_root, "one")))[0];
            var second = Import(h, SourceWav(name, 5, Path.Combine(_root, "two")))[0];

            Assert.Equal($"sounds/{name}", first);
            Assert.NotEqual(first, second);
            var names = h.Manager.Settings.SoundDisplayNames;
            Assert.Equal($"chime-{_tag}", names[second]);
            Assert.False(names.ContainsKey(first)); // same as its file name: nothing to store
            Assert.Equal($"chime-{_tag}", Item(h, second).DisplayName);
            Assert.Contains("imported", h.ViewModel.SoundLibraryMessage);

            // Import never adds files to a rule by itself.
            Assert.Empty(h.Manager.Settings.DefaultSoundRule.Files);
        });
    }

    [Fact]
    public void RemoveFromLists_KeepsFile_DeleteNeedsConfirmation_ThenRemovesFileAndReferences()
    {
        StaThreadTestHelper.Run(() =>
        {
            var h = Create(s => s.SoundEnabled = true);
            var name = $"gone-{_tag}.wav";
            Import(h, SourceWav(name, 6, Path.Combine(_root, "x")));
            var reference = Import(h, SourceWav(name, 7, Path.Combine(_root, "y")))[0]; // collision → has a display name
            var item = Item(h, reference);
            var fullPath = item.FullPath;
            item.DefaultChoice!.IsSelected = true;
            Save(h.ViewModel);
            var app = h.Manager.Settings.Clone();
            app.AppSoundRules["Slack"] = new SoundRule { Files = { reference } };
            h.Manager.Apply(app);

            h.ViewModel.RemoveSoundFromListsCommand.Execute(item);
            Assert.True(File.Exists(fullPath));
            Assert.Empty(h.Manager.Settings.DefaultSoundRule.Files);
            Assert.Empty(h.Manager.Settings.AppSoundRules["Slack"].Files);
            Assert.Empty(h.ViewModel.DefaultRuleEditor.Files);

            // Delete is refused without confirmation.
            var asked = 0;
            h.ViewModel.ConfirmSoundAction = (_, _) => { asked++; return false; };
            h.ViewModel.DeleteSoundFileCommand.Execute(Item(h, reference));
            Assert.Equal(1, asked);
            Assert.True(File.Exists(fullPath));
            Assert.True(h.Manager.Settings.SoundDisplayNames.ContainsKey(reference));

            Item(h, reference).DefaultChoice!.IsSelected = true;
            h.ViewModel.ConfirmSoundAction = (_, _) => { asked++; return true; };
            h.ViewModel.DeleteSoundFileCommand.Execute(Item(h, reference));
            Assert.Equal(2, asked);
            Assert.False(File.Exists(fullPath));
            Assert.DoesNotContain(reference, h.Manager.Settings.DefaultSoundRule.Files);
            Assert.False(h.Manager.Settings.SoundDisplayNames.ContainsKey(reference));
            Assert.DoesNotContain(h.ViewModel.SoundLibraryItems, i => i.RuleReference == reference);
        });
    }

    [Fact]
    public void Delete_IsNotOfferedForExternalFolderFiles()
    {
        StaThreadTestHelper.Run(() =>
        {
            var external = TestAppDataIsolation.CreateUniqueDirectory("ext");
            var path = SourceWav($"ext-{_tag}.wav", 8, external);
            var h = Create();
            h.ViewModel.PickSoundFolderAction = () => external;
            h.ViewModel.AddSoundFolderCommand.Execute(null);
            var item = h.ViewModel.SoundLibraryItems.Single(i => i.FullPath == path);
            Assert.False(item.IsManaged);

            h.ViewModel.ConfirmSoundAction = (_, _) => true;
            h.ViewModel.DeleteSoundFileCommand.Execute(item);
            Assert.True(File.Exists(path));
            Assert.Contains("Only files in the managed folder", h.ViewModel.SoundLibraryMessage);
        });
    }

    [Fact]
    public void AddFolder_ShowsRejectionReason_AndAcceptsLocalFolderAsReference()
    {
        StaThreadTestHelper.Run(() =>
        {
            var h = Create();
            h.ViewModel.PickSoundFolderAction = () => @"\\server\share\sounds";
            h.ViewModel.AddSoundFolderCommand.Execute(null);
            Assert.Contains("Network folders cannot be added", h.ViewModel.SoundLibraryMessage);
            Assert.Empty(h.Manager.Settings.SoundLibraryFolders);

            h.ViewModel.PickSoundFolderAction = () => Path.Combine(_root, "does-not-exist");
            h.ViewModel.AddSoundFolderCommand.Execute(null);
            Assert.Contains("does not exist", h.ViewModel.SoundLibraryMessage);

            var local = TestAppDataIsolation.CreateUniqueDirectory("local");
            var wav = SourceWav($"local-{_tag}.wav", 9, local);
            h.ViewModel.PickSoundFolderAction = () => local;
            h.ViewModel.AddSoundFolderCommand.Execute(null);
            Assert.Equal(new[] { local }, h.Manager.Settings.SoundLibraryFolders);
            Assert.Contains(h.ViewModel.SoundLibraryFolderItems, f => f.FolderPath == local && f.IsExternal);
            var item = h.ViewModel.SoundLibraryItems.Single(i => i.FullPath == wav);
            Assert.Equal("External folder", item.SourceText);
            Assert.Equal("Available", item.StatusText);
            Assert.True(File.Exists(wav)); // referenced, not copied
            Assert.False(File.Exists(Path.Combine(h.Library.ManagedRoot, Path.GetFileName(wav))));
        });
    }

    [Fact]
    public void PreviewFromUi_DoesNotConsumeSequentialOrder()
    {
        StaThreadTestHelper.Run(() =>
        {
            var h = Create(s => s.SoundEnabled = true);
            var refs = Import(h, SourceWav($"p1-{_tag}.wav", 10), SourceWav($"p2-{_tag}.wav", 11));
            Item(h, refs[0]).DefaultChoice!.IsSelected = true;
            Item(h, refs[1]).DefaultChoice!.IsSelected = true;
            h.ViewModel.DefaultRuleEditor.Mode = SoundSelectionMode.Sequential;
            Save(h.ViewModel);

            // Row preview of the second file and "Preview default sound" (first usable file).
            h.ViewModel.PreviewSoundCommand.Execute(Item(h, refs[1]));
            h.Posts.RunAll();
            h.Engine.Current!.Complete();
            h.Posts.RunAll();
            h.ViewModel.PreviewDefaultSoundCommand.Execute(null);
            h.Posts.RunAll();
            Assert.Equal(Item(h, refs[0]).FullPath, h.Engine.Plays[^1].FullPath);
            h.Engine.Current!.Complete();
            h.Posts.RunAll();
            Assert.Equal(0, h.Coordinator.Selector.HistoryCount);

            // The first real notification still starts at the top of the list.
            h.Coordinator.EnqueueNotificationSound("Any App", null);
            h.Posts.RunAll();
            Assert.Equal(Item(h, refs[0]).FullPath, h.Engine.Plays[^1].FullPath);
        });
    }

    [Fact]
    public void StatusLine_ShowsStateCountersAndLastFile_ButNoAppName()
    {
        StaThreadTestHelper.Run(() =>
        {
            var h = Create(s => s.SoundEnabled = true);
            var name = $"status-{_tag}.wav";
            Import(h, SourceWav(name, 12, Path.Combine(_root, "s1")));
            var reference = Import(h, SourceWav(name, 13, Path.Combine(_root, "s2")))[0];
            Item(h, reference).DefaultChoice!.IsSelected = true;
            Save(h.ViewModel);

            h.Coordinator.EnqueueNotificationSound("Secret Messenger", "Secret.App!Id");
            h.Posts.RunAll();
            var playing = h.ViewModel.PlaybackStatusText;
            Assert.Contains("Playback: Notification sound", playing);
            Assert.Contains("Started: 1", playing);
            Assert.Contains($"Last file: status-{_tag}", playing); // display name, not the internal name
            Assert.DoesNotContain("Secret", playing);
            Assert.DoesNotContain(@":\", playing);

            h.ViewModel.StopAllPlaybackCommand.Execute(null);
            h.Posts.RunAll();
            Assert.Contains("Playback: Idle", h.ViewModel.PlaybackStatusText);
            Assert.Contains("Last: stopped", h.ViewModel.PlaybackStatusText);
        });
    }

    [Fact]
    public void EnteringSoundsTab_RescansLibrary()
    {
        StaThreadTestHelper.Run(() =>
        {
            var h = Create();
            var path = TestWavFactory.Write(h.Library.ManagedRoot, $"dropped-{_tag}.wav", TestWavFactory.Pcm(0.1, fill: 14));
            Assert.DoesNotContain(h.ViewModel.SoundLibraryItems, i => i.FullPath == path);

            h.ViewModel.IsSoundsTabSelected = true;
            var item = h.ViewModel.SoundLibraryItems.Single(i => i.FullPath == path);
            Assert.Equal("Managed folder", item.SourceText);
            Assert.Equal("0.1 s", item.DurationText);
            Assert.False(item.DefaultChoice!.IsSelected); // new files are never auto-added to a rule
        });
    }

    [Fact]
    public void ClearOverrides_AlsoReturnsAppToDefaultSoundRule()
    {
        StaThreadTestHelper.Run(() =>
        {
            var h = Create(s => s.AppSoundRules["Teams"] = new SoundRule { ExplicitSilence = true });
            h.ViewModel.RefreshPerAppConfig();
            var entry = h.ViewModel.PerAppConfigEntries.Single(e => e.AppName == "Teams");
            Assert.True(entry.HasOverrides);
            Assert.Equal(AppSoundRuleChoice.Silence, entry.SoundRule!.Choice);

            h.ViewModel.ClearPerAppOverridesCommand.Execute(entry);
            Assert.False(h.Manager.Settings.AppSoundRules.ContainsKey("Teams"));
        });
    }

    [Fact]
    public void AppRuleKeyedByAumid_IsShownUnderItsDisplayName_OrAsItsOwnEntryWhenUnseen()
    {
        StaThreadTestHelper.Run(() =>
        {
            var h = Create(s =>
            {
                s.AppSoundRules["Seen.App!Main"] = new SoundRule { ExplicitSilence = true };
                s.AppSoundRules["Unseen.App!Main"] = new SoundRule { Files = { "sounds/u.wav" } };
                s.ManualMuteConfirmations["Seen.App!Main"] = new ManualMuteConfirmation { State = ManualMuteConfirmation.UserConfirmed };
            });
            h.Queue.AddNotification("Seen App", "title", "body", "Seen.App!Main");
            Assert.Equal("Seen App", h.Queue.AppIdentities.FindDisplayNameForAppId("seen.app!main"));
            Assert.Null(h.Queue.AppIdentities.FindDisplayNameForAppId("Unseen.App!Main"));

            h.ViewModel.RefreshPerAppConfig();
            var names = h.ViewModel.PerAppConfigEntries.Select(e => e.AppName).ToList();
            Assert.Contains("Seen App", names);
            Assert.DoesNotContain("Seen.App!Main", names);
            Assert.Contains("Unseen.App!Main", names);

            var seen = h.ViewModel.PerAppConfigEntries.Single(e => e.AppName == "Seen App").SoundRule!;
            Assert.Equal("Seen.App!Main", seen.RuleKey);
            Assert.Equal(AppSoundRuleChoice.Silence, seen.Choice);
            Assert.False(seen.HasMultipleAppIds);
            Assert.Equal("Confirmed by you (not verified by the app)", seen.OriginalSoundStatusText);
            Assert.Equal(AppSoundRuleChoice.Custom,
                h.ViewModel.PerAppConfigEntries.Single(e => e.AppName == "Unseen.App!Main").SoundRule!.Choice);
        });
    }
}
