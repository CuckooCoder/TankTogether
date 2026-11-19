using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using TankTogether.Networking;
using UnityEditor;
using UnityEngine;
using Save = TankTogether.Networking.CoopCampaignSave;

namespace TankTogether.Editor
{
    public static class CoopCampaignSaveChecks
    {
        [Serializable] public sealed class Report
        {
            public string utc, temporaryRoot, error;
            public bool complete;
            public List<string> passed = new List<string>();
        }
        [Serializable] sealed class Envelope
        {
            public int envelopeVersion, schemaVersion;
            public string payload, sha256;
        }
        static string Hash(string value)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "").ToLowerInvariant();
        }
        static bool Catalog(Save.Progress progress) => progress.levelId == "steam-lab" &&
            (progress.checkpointId == "start" || progress.checkpointId == "cp1" || progress.checkpointId == "cp2");
        static string Encoded(Save.Progress progress)
        {
            string payload = JsonUtility.ToJson(progress);
            return JsonUtility.ToJson(new Envelope {
                envelopeVersion = 1, schemaVersion = progress.schemaVersion, payload = payload, sha256 = Hash(payload)
            }, true);
        }

        [MenuItem("Tank Together/Steam/Check Campaign Save")]
        public static void Menu() { Debug.Log(Check()); }

        // All writes use a unique temporary test directory, never the player's real save directory.
        public static string Check()
        {
            const ulong owner = 76561198000000001UL, other = 76561198000000002UL;
            const string campaign = "steam-lab", content = "test-content-1";
            string root = Path.Combine(Path.GetTempPath(), "TankTogether-SaveChecks-" + Guid.NewGuid().ToString("N"));
            var report = new Report { utc = DateTime.UtcNow.ToString("O"), temporaryRoot = root };
            void Assert(bool condition, string name)
            {
                if (!condition) throw new InvalidOperationException(name);
                report.passed.Add(name);
            }
            try
            {
                var missing = Save.Load(owner, campaign, content, Catalog, root);
                Assert(!missing.success && missing.status == Save.Status.Missing, "missing_is_explicit_not_created");
                Assert(Save.Create(0, campaign, "steam-lab", "start", content, Catalog, root).status == Save.Status.Invalid,
                    "zero_owner_rejected");
                Assert(Save.Create(owner, "../escape", "steam-lab", "start", content, Catalog, root).status == Save.Status.Invalid,
                    "campaign_path_traversal_rejected");
                Assert(Save.Create(owner, campaign, "steam-lab", "unknown", content, Catalog, root).status == Save.Status.UnknownCheckpoint,
                    "create_checks_session_checkpoint_catalog");
                var created = Save.Create(owner, campaign, "steam-lab", "start", content, Catalog, root);
                Assert(created.success && created.status == Save.Status.Created && created.progress.revision == 1,
                    "create_returns_verified_initial_revision");
                string path = created.path;
                Assert(!path.StartsWith(Save.DefaultRoot, StringComparison.OrdinalIgnoreCase), "test_uses_isolated_directory");
                var loaded = Save.Load(owner, campaign, content, Catalog, root);
                Assert(loaded.success && loaded.progress.ownerSteamId == owner.ToString() && loaded.progress.checkpointId == "start",
                    "load_round_trip_host_campaign_content_checkpoint");
                Assert(Save.Create(owner, campaign, "steam-lab", "start", content, Catalog, root).status == Save.Status.AlreadyExists,
                    "new_game_cannot_silently_overwrite_existing");
                Assert(Save.Load(other, campaign, content, Catalog, root).status == Save.Status.Missing,
                    "steam_accounts_are_isolated");
                var proposed = Save.Copy(loaded.progress);
                proposed.checkpointId = "cp1";
                var saved = Save.Save(proposed, Catalog, root);
                Assert(saved.success && saved.status == Save.Status.Saved && saved.progress.revision == 2 &&
                    proposed.revision == 1, "save_uses_copy_returns_incremented_revision");
                Assert(File.Exists(path + ".bak") && Save.Load(owner, campaign, content, Catalog, root).progress.checkpointId == "cp1",
                    "atomic_replace_keeps_previous_backup");
                string stableBytes = File.ReadAllText(path);
                Assert(Save.Save(proposed, Catalog, root).status == Save.Status.Conflict &&
                    File.ReadAllText(path) == stableBytes, "stale_revision_cannot_overwrite_newer_progress");
                using (var guard = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                    Assert(Save.Save(saved.progress, Catalog, root).status == Save.Status.IoError &&
                        File.ReadAllText(path) == stableBytes, "another_process_write_lock_prevents_concurrent_overwrite");
                var invalid = Save.Copy(saved.progress); invalid.checkpointId = "unregistered";
                Assert(Save.Save(invalid, Catalog, root).status == Save.Status.UnknownCheckpoint &&
                    File.ReadAllText(path) == stableBytes, "save_unknown_checkpoint_rejected_without_write");
                Assert(Save.Load(owner, campaign, "different-content", Catalog, root).status == Save.Status.IncompatibleContent,
                    "content_mismatch_does_not_roll_back_to_backup");
                var wrongVersion = Save.Copy(saved.progress); wrongVersion.contentVersion = "different-content";
                Assert(Save.Save(wrongVersion, Catalog, root).status == Save.Status.IncompatibleContent &&
                    File.ReadAllText(path) == stableBytes, "save_content_change_requires_explicit_migration");
                File.WriteAllText(path + ".leftover.tmp", "interrupted-write");
                Assert(Save.Load(owner, campaign, content, Catalog, root).success, "unfinished_temporary_file_is_not_loaded");
                var tampered = JsonUtility.FromJson<Envelope>(stableBytes);
                var tamperedProgress = JsonUtility.FromJson<Save.Progress>(tampered.payload);
                tamperedProgress.checkpointId = "cp2";
                tampered.payload = JsonUtility.ToJson(tamperedProgress);
                File.WriteAllText(path, JsonUtility.ToJson(tampered));
                string damagedBytes = File.ReadAllText(path);
                var recovered = Save.Load(owner, campaign, content, Catalog, root);
                Assert(recovered.success && recovered.status == Save.Status.RecoveredBackup &&
                    recovered.progress.checkpointId == "start" && File.ReadAllText(path) == damagedBytes,
                    "checksum_corruption_recovers_backup_without_mutating_disk");
                var restore = Save.Copy(recovered.progress); restore.checkpointId = "cp2";
                var restored = Save.Save(restore, Catalog, root);
                Assert(restored.success && restored.status == Save.Status.SavedAfterRecovery &&
                    Directory.GetFiles(Path.GetDirectoryName(path), campaign + ".json.corrupt-*").Length == 1,
                    "explicit_recovery_save_preserves_damaged_primary");
                Assert(Save.Load(owner, campaign, content, Catalog, root).progress.checkpointId == "cp2",
                    "recovered_save_read_back");
                string validPrimary = File.ReadAllText(path), validBackup = File.ReadAllText(path + ".bak");
                File.Delete(path);
                Assert(Save.Load(owner, campaign, content, Catalog, root).status == Save.Status.RecoveredBackup,
                    "missing_primary_can_recover_backup");
                File.WriteAllText(path, validPrimary);
                var future = Save.Copy(restored.progress); future.schemaVersion = Save.CurrentSchema + 1;
                File.WriteAllText(path, Encoded(future));
                string futureBytes = File.ReadAllText(path);
                Assert(Save.Load(owner, campaign, content, Catalog, root).status == Save.Status.FutureSchema &&
                    Save.Save(restored.progress, Catalog, root).status == Save.Status.FutureSchema &&
                    Save.Create(owner, campaign, "steam-lab", "start", content, Catalog, root).status == Save.Status.FutureSchema &&
                    File.ReadAllText(path) == futureBytes, "future_schema_never_read_rolled_back_or_overwritten");
                File.WriteAllText(path, validPrimary); File.WriteAllText(path + ".bak", Encoded(future));
                Assert(Save.Save(restored.progress, Catalog, root).status == Save.Status.FutureSchema &&
                    File.ReadAllText(path + ".bak") == Encoded(future), "future_backup_also_protected");
                File.WriteAllText(path + ".bak", validBackup);
                var unknown = Save.Copy(restored.progress); unknown.checkpointId = "removed-checkpoint";
                File.WriteAllText(path, Encoded(unknown));
                Assert(Save.Load(owner, campaign, content, Catalog, root).status == Save.Status.UnknownCheckpoint,
                    "valid_but_unknown_checkpoint_never_silently_falls_back");
                File.WriteAllText(path, validPrimary);
                var another = Save.Create(other, campaign, "steam-lab", "start", content, Catalog, root);
                Assert(another.success, "second_host_can_create_independent_progress");
                File.Copy(path, another.path, true);
                Assert(Save.Load(other, campaign, content, Catalog, root).status == Save.Status.Invalid,
                    "copied_other_hosts_payload_rejected");
                File.WriteAllText(path, "broken-primary"); File.WriteAllText(path + ".bak", "broken-backup");
                Assert(Save.Load(owner, campaign, content, Catalog, root).status == Save.Status.Corrupt &&
                    Save.Create(owner, campaign, "steam-lab", "start", content, Catalog, root).status == Save.Status.Corrupt &&
                    Save.Save(restored.progress, Catalog, root).status == Save.Status.Corrupt &&
                    File.ReadAllText(path) == "broken-primary" && File.ReadAllText(path + ".bak") == "broken-backup",
                    "double_corruption_blocks_create_save_and_preserves_evidence");
                string fileAsRoot = Path.Combine(root, "not-a-directory");
                File.WriteAllText(fileAsRoot, "keep");
                Assert(Save.Create(owner, campaign, "steam-lab", "start", content, Catalog, fileAsRoot).status == Save.Status.IoError &&
                    File.ReadAllText(fileAsRoot) == "keep", "unwritable_path_reports_io_failure_without_success");
                Assert(!validPrimary.Contains("position") && !validPrimary.Contains("velocity") &&
                    !validPrimary.Contains("lobby") && !validPrimary.Contains("grip"), "save_has_no_live_physics_or_network_state");
                report.complete = true;
            }
            catch (Exception e) { report.error = e.ToString(); }
            finally
            {
                // Guard the resolved absolute target before recursive cleanup; remove only this unique test root.
                string resolved = Path.GetFullPath(root);
                string tempParent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (resolved.StartsWith(tempParent, StringComparison.OrdinalIgnoreCase) &&
                    Path.GetFileName(resolved).StartsWith("TankTogether-SaveChecks-", StringComparison.Ordinal) &&
                    Directory.Exists(resolved))
                {
                    try { Directory.Delete(resolved, true); }
                    catch (Exception e) { report.error = (report.error ?? "") + "\n临时测试目录清理失败：" + e.Message; report.complete = false; }
                }
            }
            string json = JsonUtility.ToJson(report, true);
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/coop-campaign-save-checks.json", json, new UTF8Encoding(false));
            return json;
        }
    }
}
