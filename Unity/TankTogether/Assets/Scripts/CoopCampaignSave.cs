using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace TankTogether.Networking
{
    /// <summary>Host-owned checkpoint progress only. Live bodies, inputs and lobby state are never saved.</summary>
    public static class CoopCampaignSave
    {
        public const int CurrentSchema = 2;
        const int EnvelopeVersion = 1, MaxFileBytes = 8 * 1024 * 1024;
        static readonly Regex StableId = new Regex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,79}$");
        static readonly object Gate = new object();

        public enum Status
        {
            Loaded, RecoveredBackup, Created, Saved, SavedAfterRecovery,
            Missing, AlreadyExists, Invalid, Corrupt, FutureSchema,
            IncompatibleContent, UnknownCheckpoint, Conflict, IoError
        }

        [Serializable]
        public sealed class Progress
        {
            public int schemaVersion;
            public string ownerSteamId, campaignId, levelId, checkpointId, contentVersion, savedAtUtc;
            public long revision;
            public string displayName;
            public bool runInitialized;
            public AdventureRun run;
            public AdventureReport pendingReport;
            public System.Collections.Generic.List<AdventureReport> history = new System.Collections.Generic.List<AdventureReport>();
        }

        [Serializable]
        public sealed class Result
        {
            public bool success;
            public Status status;
            public string message, path;
            public Progress progress;
        }

        [Serializable]
        sealed class Envelope
        {
            public int envelopeVersion, schemaVersion;
            public string payload, sha256;
        }

        sealed class Candidate
        {
            public bool exists;
            public Status status;
            public string message;
            public Progress progress;
        }

        public static string DefaultRoot => Path.Combine(Application.persistentDataPath, "CoopCampaigns");

        public static Progress Copy(Progress source)
        {
            if (source == null) return null;
            var copy=JsonUtility.FromJson<Progress>(JsonUtility.ToJson(source));
            // JsonUtility can materialize missing nested serializable objects. Preserve actual absence.
            if(source.run==null)copy.run=null;
            if(source.pendingReport==null)copy.pendingReport=null;
            return copy;
        }

        public static Result Load(ulong ownerSteamId, string campaignId, string contentVersion,
            Func<Progress, bool> validateCheckpoint = null, string rootDirectory = null)
        {
            lock (Gate)
            {
                if (!TryPath(ownerSteamId, campaignId, rootDirectory, out string path, out string error))
                    return Outcome(Status.Invalid, error, path);
                if (!ValidVersion(contentVersion)) return Outcome(Status.Invalid, "内容版本不能为空或过长。", path);
                return Read(path, ownerSteamId, campaignId, contentVersion, validateCheckpoint);
            }
        }

        /// <summary>Never replaces an existing or damaged save. Defaults are supplied by the session catalog.</summary>
        public static Result Create(ulong ownerSteamId, string campaignId, string levelId,
            string checkpointId, string contentVersion, Func<Progress, bool> validateCheckpoint = null,
            string rootDirectory = null)
        {
            lock (Gate)
            {
                if (!TryPath(ownerSteamId, campaignId, rootDirectory, out string path, out string error))
                    return Outcome(Status.Invalid, error, path);
                var progress = new Progress {
                    schemaVersion = CurrentSchema, ownerSteamId = ownerSteamId.ToString(CultureInfo.InvariantCulture),
                    campaignId = campaignId, levelId = levelId, checkpointId = checkpointId,
                    contentVersion = contentVersion, revision = 1, savedAtUtc = DateTime.UtcNow.ToString("O")
                };
                var invalid = Validate(progress, ownerSteamId, campaignId, contentVersion, validateCheckpoint);
                if (invalid != null) return Outcome(invalid.status, invalid.message, path);
                return WithWriteLock(path, () => {
                    var existing = Read(path, ownerSteamId, campaignId, contentVersion, validateCheckpoint);
                    if (existing.status != Status.Missing)
                        return existing.success
                            ? Outcome(Status.AlreadyExists, "已有进度，请读取继续；新建不会覆盖已有存档。", path, existing.progress)
                            : existing;
                    return Write(path, progress, false, false);
                });
            }
        }

        /// <summary>
        /// Saves a copy after revision and catalog validation. RecoveredBackup may be saved explicitly;
        /// the damaged primary is preserved as a .corrupt file and the result reports recovery.
        /// </summary>
        public static Result Save(Progress progress, Func<Progress, bool> validateCheckpoint = null,
            string rootDirectory = null)
        {
            lock (Gate)
            {
                if (progress == null || !ulong.TryParse(progress.ownerSteamId, NumberStyles.None,
                    CultureInfo.InvariantCulture, out ulong owner))
                    return Outcome(Status.Invalid, "存档缺少有效的房主 Steam ID。", null);
                if (!TryPath(owner, progress.campaignId, rootDirectory, out string path, out string error))
                    return Outcome(Status.Invalid, error, path);
                var invalid = Validate(progress, owner, progress.campaignId, progress.contentVersion, validateCheckpoint);
                if (invalid != null) return Outcome(invalid.status, invalid.message, path);
                return WithWriteLock(path, () => {
                    var previous = Read(path, owner, progress.campaignId, progress.contentVersion, validateCheckpoint);
                    if (!previous.success) return previous;
                    if (progress.revision != previous.progress.revision)
                        return Outcome(Status.Conflict, "存档已被更新，请重新读取；没有覆盖较新进度。", path, previous.progress);
                    if (previous.progress.revision == long.MaxValue)
                        return Outcome(Status.Invalid, "存档修订号已超出范围。", path);
                    var next = Copy(progress);
                    next.schemaVersion = CurrentSchema;
                    // Preserve the original legacy file before upgrading. Old builds reject schema 2.
                    if (previous.progress.schemaVersion == 1 && File.Exists(path) && !File.Exists(path + ".legacy-v1"))
                        File.Copy(path, path + ".legacy-v1", false);
                    next.revision++;
                    next.savedAtUtc = DateTime.UtcNow.ToString("O");
                    return Write(path, next, true, previous.status == Status.RecoveredBackup);
                });
            }
        }

        public static string SlotCampaign(string level, int slot)
        {
            if (slot < 1 || slot > 5 || !ValidId(level)) throw new ArgumentOutOfRangeException(nameof(slot));
            return slot == 1 ? "prototype-" + level : "slot-" + slot + "-" + level;
        }

        // Called only after the UI's second confirmation; revision binds it to the reviewed save.
        public static Result Delete(ulong owner, string campaign, string content, long expectedRevision,
            Func<Progress,bool> catalog = null, string rootDirectory = null)
        {
            lock (Gate)
            {
                if (!TryPath(owner, campaign, rootDirectory, out string path, out string error)) return Outcome(Status.Invalid,error,path);
                return WithWriteLock(path, () => {
                    var existing = Read(path, owner, campaign, content, catalog);
                    if (!existing.success) return existing;
                    if (existing.progress.revision != expectedRevision) return Outcome(Status.Conflict,"存档已变化，请重新查看后再删除。",path);
                    // Backup first: a crash cannot resurrect a deleted primary from its old backup.
                    foreach (string suffix in new[]{".bak",".legacy-v1",""}) if(File.Exists(path+suffix)) File.Delete(path+suffix);
                    return Outcome(Status.Saved,"已删除该存档及历史。",path);
                });
            }
        }

        static Result Read(string path, ulong owner, string campaign, string content,
            Func<Progress, bool> validateCheckpoint)
        {
            Candidate main = ReadCandidate(path);
            Candidate backup = ReadCandidate(path + ".bak");
            // Fail closed even if the older primary is readable: never overwrite a newer-format backup.
            if (main.status == Status.FutureSchema || backup.status == Status.FutureSchema)
                return Outcome(Status.FutureSchema, "存档或备份来自更新版本；当前程序拒绝读取或覆盖。", path);
            if (!main.exists && !backup.exists) return Outcome(Status.Missing, "没有该房主的战役存档。", path);
            if (main.status == Status.Loaded)
            {
                var invalid = Validate(main.progress, owner, campaign, content, validateCheckpoint);
                return invalid == null
                    ? Outcome(Status.Loaded, "已读取房主进度。", path, main.progress)
                    : Outcome(invalid.status, invalid.message, path);
            }
            // Identity/content/catalog mismatches are not corruption and must not cause a rollback.
            if (backup.status == Status.Loaded)
            {
                var invalid = Validate(backup.progress, owner, campaign, content, validateCheckpoint);
                if (invalid != null) return Outcome(invalid.status, invalid.message, path);
                return Outcome(Status.RecoveredBackup,
                    "主存档缺失或损坏，已读取上一份有效备份；本次读取没有修改原文件。", path, backup.progress);
            }
            var status = main.status == Status.IoError || backup.status == Status.IoError ? Status.IoError : Status.Corrupt;
            return Outcome(status, "存档及备份无法读取；已保留原文件，未新建或覆盖。主档：" +
                main.message + "；备份：" + backup.message, path);
        }

        static Candidate ReadCandidate(string path)
        {
            var result = new Candidate { status = Status.Missing, message = "不存在" };
            try
            {
                // File.Exists suppresses access errors, so use FileStream and distinguish missing from IO failure.
                string text;
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
                {
                    result.exists = true;
                    if (stream.Length < 2 || stream.Length > MaxFileBytes)
                        throw new InvalidDataException("文件大小不合法");
                    using (var reader = new StreamReader(stream, Encoding.UTF8, true))
                        text = reader.ReadToEnd();
                }
                var envelope = JsonUtility.FromJson<Envelope>(text);
                if (envelope == null) throw new InvalidDataException("缺少存档封装");
                if (envelope.envelopeVersion > EnvelopeVersion || envelope.schemaVersion > CurrentSchema)
                {
                    result.status = Status.FutureSchema; result.message = "更新的存档格式"; return result;
                }
                if (envelope.envelopeVersion != EnvelopeVersion || envelope.schemaVersion < 1 ||
                    string.IsNullOrEmpty(envelope.payload)) throw new InvalidDataException("存档格式不合法");
                var progress = JsonUtility.FromJson<Progress>(envelope.payload);
                if (progress != null && progress.schemaVersion > CurrentSchema)
                {
                    result.status = Status.FutureSchema; result.message = "更新的进度格式"; return result;
                }
                if (progress == null || progress.schemaVersion != envelope.schemaVersion)
                    throw new InvalidDataException("进度格式不匹配");
                if (!string.Equals(Hash(envelope.payload), envelope.sha256, StringComparison.Ordinal))
                    throw new InvalidDataException("SHA256 校验失败");
                if(progress.schemaVersion==1 || !progress.runInitialized)progress.run=null;
                if(progress.pendingReport==null || string.IsNullOrEmpty(progress.pendingReport.runId))progress.pendingReport=null;
                result.status = Status.Loaded; result.progress = progress; result.message = "校验通过"; return result;
            }
            catch (FileNotFoundException) { return result; }
            catch (DirectoryNotFoundException) { return result; }
            catch (UnauthorizedAccessException e)
            {
                result.exists = true; result.status = Status.IoError; result.message = e.Message; return result;
            }
            catch (InvalidDataException e)
            {
                result.exists = true; result.status = Status.Corrupt; result.message = e.Message; return result;
            }
            catch (IOException e)
            {
                result.exists = true; result.status = Status.IoError; result.message = e.Message; return result;
            }
            catch (Exception e)
            {
                result.exists = true; result.status = Status.Corrupt; result.message = e.Message; return result;
            }
        }

        static Result Validate(Progress p, ulong owner, string campaign, string content,
            Func<Progress, bool> validateCheckpoint)
        {
            if (p.schemaVersion > CurrentSchema) return Outcome(Status.FutureSchema, "进度来自更新版本。", null);
            if (p.schemaVersion < 1 || p.revision < 1 ||
                p.ownerSteamId != owner.ToString(CultureInfo.InvariantCulture) || p.campaignId != campaign ||
                !ValidId(p.levelId) || !ValidId(p.checkpointId) || !ValidVersion(p.contentVersion) ||
                !DateTime.TryParse(p.savedAtUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _))
                return Outcome(Status.Invalid, "存档身份、稳定标识或基础字段不合法。", null);
            if (p.run != null && (string.IsNullOrEmpty(p.run.id) || p.run.seconds < 0 || double.IsNaN(p.run.seconds) || double.IsInfinity(p.run.seconds) || p.run.breaks < 0 || p.run.checkpoints < 0))
                return Outcome(Status.Invalid, "冒险统计字段不合法。", null);
            if (!ValidVersion(content) || p.contentVersion != content)
                return Outcome(Status.IncompatibleContent, "存档内容版本不同，请进行显式迁移；没有回退或覆盖。", null);
            try
            {
                if (validateCheckpoint != null && !validateCheckpoint(Copy(p)))
                    return Outcome(Status.UnknownCheckpoint, "关卡或检查点不在当前会话目录中。", null);
            }
            catch (Exception e)
            {
                return Outcome(Status.UnknownCheckpoint, "检查点目录验证失败：" + e.Message, null);
            }
            return null;
        }

        static Result WithWriteLock(string path, Func<Result> transaction)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                // The revision check and replacement are one transaction across local game processes.
                // An empty .lock file may remain after exit; only the live exclusive handle holds the lock.
                using (var guard = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                    return transaction();
            }
            catch (Exception e)
            {
                return Outcome(Status.IoError, "无法取得存档写入权限或其他实例正在保存；未覆盖进度：" + e.Message, path);
            }
        }

        static Result Write(string path, Progress progress, bool replace, bool recovered)
        {
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            string preserved = null;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                progress.runInitialized=progress.run!=null&&!string.IsNullOrEmpty(progress.run.id);
                string payload = JsonUtility.ToJson(progress);
                string json = JsonUtility.ToJson(new Envelope {
                    envelopeVersion = EnvelopeVersion, schemaVersion = CurrentSchema,
                    payload = payload, sha256 = Hash(payload)
                }, true);
                byte[] bytes = new UTF8Encoding(false).GetBytes(json);
                using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    file.Write(bytes, 0, bytes.Length);
                    file.Flush(true);
                }
                // Verify exact on-disk bytes before touching the previous save.
                var verified = ReadCandidate(temporary);
                if (verified.status != Status.Loaded) throw new InvalidDataException("临时存档写入后校验失败");
                if (!replace) File.Move(temporary, path);
                else if (recovered)
                {
                    if (File.Exists(path))
                    {
                        preserved = path + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff") +
                            "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                        File.Copy(path, preserved, false);
                        File.Replace(temporary, path, null);
                    }
                    else File.Move(temporary, path);
                    // Keep the known-good .bak rather than replacing it with the damaged primary.
                }
                else File.Replace(temporary, path, path + ".bak");
                return Outcome(!replace ? Status.Created : recovered ? Status.SavedAfterRecovery : Status.Saved,
                    !replace ? "已新建房主进度。" : recovered
                    ? "已从有效备份继续保存；原损坏文件已保留。" + (preserved ?? "")
                    : "已原子保存检查点进度，上一版本保留为备份。", path, progress);
            }
            catch (Exception e)
            {
                return Outcome(Status.IoError, "存档写入失败，请重新读取核对；没有静默新建替代进度：" + e.Message, path);
            }
            finally
            {
                // Only this operation's own unique temporary file is eligible for cleanup.
                try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
            }
        }

        static bool TryPath(ulong owner, string campaign, string root, out string path, out string error)
        {
            path = null; error = null;
            if (owner == 0 || !ValidId(campaign))
            {
                error = "房主 Steam ID 或战役稳定标识不合法。"; return false;
            }
            try
            {
                string directory = Path.GetFullPath(root ?? DefaultRoot);
                path = Path.Combine(directory, owner.ToString(CultureInfo.InvariantCulture), campaign + ".json");
                return true;
            }
            catch (Exception e) { error = "存档目录不合法：" + e.Message; return false; }
        }

        static bool ValidId(string value) => value != null && StableId.IsMatch(value);
        static bool ValidVersion(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 128;
        static string Hash(string value)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "").ToLowerInvariant();
        }
        static Result Outcome(Status status, string message, string path, Progress progress = null)
        {
            return new Result {
                success = status == Status.Loaded || status == Status.RecoveredBackup || status == Status.Created ||
                    status == Status.Saved || status == Status.SavedAfterRecovery,
                status = status, message = message, path = path, progress = Copy(progress)
            };
        }
    }
}
