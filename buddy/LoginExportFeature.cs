using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace HeartopiaMod
{
    // ============================================================================================
    // LOGIN EXPORT  (account transfer helper)
    //
    // WHY: the XD SDK login cache
    //     %LocalLow%\xd\Heartopia\XD\PC\user_v2          (encrypted XDUser: userId, name, loginType)
    //     %LocalLow%\xd\Heartopia\XD\PC\access_token_v2  (encrypted AccessToken: kid, macKey, ...)
    // is AES-256-CBC, keyed by SHA256(UTF8(SystemInfo.deviceUniqueIdentifier)) — Unity's per-device
    // id (decompiled from GameAssembly.dll: EncryptionUtils.GetAesKey = SHA256(UTF8(key)); every
    // Encrypt/Decrypt caller passes SystemInfo.deviceUniqueIdentifier). So the files cannot be
    // moved to another PC by copying — the key is different there. To transfer, decrypt here and
    // re-encrypt on the target PC under ITS device id (tools/xd_login_transfer.py).
    //
    // WHAT: this button dumps, for the CURRENT machine:
    //   - deviceUniqueIdentifier  (needed to re-encrypt on the target PC; put on the clipboard)
    //   - the decrypted user_v2 / access_token_v2 JSON, as tools/xd_login_transfer.py's bundle
    //     format ({ "user_v2": "...", "access_token_v2": "..." }) -> heartopia_login_bundle.json
    //   - a human-readable copy -> heartopia_login_export.txt
    // Both written to the mod's Bugtopia folder (HelperPaths, %LocalLow%\Bugtopia), NOT the game's.
    // Everything stays LOCAL (two files + the clipboard); nothing is sent.
    //
    // SECURITY: the output contains the live session credential (kid/macKey). Treat those files
    // like a password; delete them after the transfer.
    //
    // Format is byte-identical to the game (verified by round-trip): base64( IV[16] || AES-256-CBC /
    // PKCS7 ), key = SHA256(UTF8(deviceId)), plaintext = UTF-8 JSON.
    // ============================================================================================
    internal static class LoginExportFeature
    {
        private static readonly string[] Files = { "user_v2", "access_token_v2" };

        // Runs the export. Returns a short status string for the UI; never throws.
        public static string Export()
        {
            try
            {
                string deviceId = SystemInfo.deviceUniqueIdentifier;
                // Source cache lives in the GAME's data folder (...\LocalLow\xd\Heartopia\XD\PC).
                string pcDir = Path.Combine(Path.Combine(Application.persistentDataPath, "XD"), "PC");

                var human = new StringBuilder();
                var bundle = new StringBuilder();
                human.Append("deviceUniqueIdentifier: ").Append(deviceId).Append('\n')
                     .Append("(offline: python tools/xd_login_transfer.py import <deviceId_TARGET> heartopia_login_bundle.json)\n\n");
                bundle.Append("{\n  \"_deviceId_source\": ").Append(JsonStr(deviceId)).Append(',');

                int ok = 0;
                for (int i = 0; i < Files.Length; i++)
                {
                    string name = Files[i];
                    string path = Path.Combine(pcDir, name);
                    string json;
                    if (!File.Exists(path))
                    {
                        json = null;
                        human.Append(name).Append(": <missing>\n\n");
                    }
                    else
                    {
                        try
                        {
                            json = Decrypt(File.ReadAllText(path), deviceId);
                            ok++;
                            human.Append(name).Append(":\n").Append(json).Append("\n\n");
                        }
                        catch (Exception e)
                        {
                            json = null;
                            human.Append(name).Append(": <decrypt failed: ").Append(e.Message).Append(">\n\n");
                        }
                    }
                    bundle.Append("\n  ").Append(JsonStr(name)).Append(": ")
                          .Append(json == null ? "null" : JsonStr(json))
                          .Append(i < Files.Length - 1 ? "," : "");
                }
                bundle.Append("\n}\n");

                // Output goes to the mod's own Bugtopia folder (%LocalLow%\Bugtopia), not the game's.
                string bundlePath = HelperPaths.GetFile("heartopia_login_bundle.json");
                string humanPath = HelperPaths.GetFile("heartopia_login_export.txt");
                File.WriteAllText(bundlePath, bundle.ToString(), new UTF8Encoding(false));
                File.WriteAllText(humanPath, human.ToString(), new UTF8Encoding(false));
                try { GUIUtility.systemCopyBuffer = deviceId; } catch { }

                ModLogger.Msg("[LoginExport] deviceUniqueIdentifier=" + deviceId + " (copied to clipboard)");
                ModLogger.Msg("[LoginExport] bundle -> " + bundlePath + " (" + ok + "/2 decrypted)");
                ModLogger.Msg("[LoginExport] human  -> " + humanPath);
                return "device id copied to clipboard.\n" + ok + "/2 decrypted -> heartopia_login_bundle.json in the Bugtopia folder.";
            }
            catch (Exception e)
            {
                ModLogger.Msg("[LoginExport] FAILED: " + e);
                return "export failed: " + e.Message;
            }
        }

        // AES-256-CBC / PKCS7, IV = first 16 bytes, key = SHA256(UTF8(deviceId)). Mirrors
        // XD.SDK.Common.Internal.EncryptionUtils.Decrypt exactly.
        private static string Decrypt(string blobBase64, string deviceId)
        {
            byte[] raw = Convert.FromBase64String(blobBase64.Trim());
            if (raw.Length < 32 || (raw.Length % 16) != 0)
                throw new InvalidDataException("bad blob length " + raw.Length);
            byte[] iv = new byte[16];
            Buffer.BlockCopy(raw, 0, iv, 0, 16);
            byte[] ct = new byte[raw.Length - 16];
            Buffer.BlockCopy(raw, 16, ct, 0, ct.Length);

            byte[] key;
            using (SHA256 sha = SHA256.Create())
                key = sha.ComputeHash(Encoding.UTF8.GetBytes(deviceId));

            using (Aes aes = Aes.Create())
            {
                aes.Key = key;
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                using (ICryptoTransform dec = aes.CreateDecryptor())
                {
                    byte[] pt = dec.TransformFinalBlock(ct, 0, ct.Length);
                    return Encoding.UTF8.GetString(pt);
                }
            }
        }

        // Minimal JSON string literal encoder (no dependency on the game's Newtonsoft).
        private static string JsonStr(string s)
        {
            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }
    }
}
