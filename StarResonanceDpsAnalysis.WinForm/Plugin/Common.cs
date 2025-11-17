using System;
using System.Security.Cryptography;

using Flurl;
using Flurl.Http;
using Newtonsoft.Json.Linq;
using StarResonanceDpsAnalysis.WinForm.Plugin.DamageStatistics;

namespace StarResonanceDpsAnalysis.WinForm.Plugin
{
    public class Common
    {



        public static string FormatSeconds(double sec)
        {
            var ts = TimeSpan.FromSeconds(sec);
            return $"{(int)ts.TotalMinutes:D2}:{ts.Seconds:D2}";
        }

        private static readonly Dictionary<string, List<ulong>> professionSkills = new()
        {
            { "Soul Musician", new List<ulong> {
                 2301, 2302, 2303, 2304, 2313, 2332, 2336, 23401, 23501,
                55301, 55302, 55304, 55314, 55339, 55341, 55342,
                230101, 230401, 230501, 230901, 2031111
            }},
            { "Marksman", new List<ulong> {
                2233, 2288, 2289, 2295, 55231,
                220101, 220102, 220104, 220109, 220110,
                1700824, 1700826, 1700827, 2203101, 2203512
            }},
            { "Shield Knight", new List<ulong> {
                 2401, 2402, 2403, 2404, 2405, 2407,
                2410, 2412, 2421,
                55404, 55412, 55421, 240101, 240102
            }},
            { "Stormblade", new List<ulong> {
                1705, 1713, 1717, 1718, 1719, 1724, 2410, 44701
            }},
            { "Frost Mage", new List<ulong> {
                1203, 1240, 1248, 1250, 1256, 1257, 1259, 1262, 1263,
                27009, 120201, 120301, 120401, 120501,
                120901, 120902, 121302, 121501, 2204081, 2204241
            }},
            { "Verdant Oracle", new List<ulong> {
                1501, 1502, 1503, 1504, 1529, 1560,
                20301, 21404, 21406, 2202091,
                150103, 150104, 150106, 150107, 1550
            }},
            { "Wind Knight", new List<ulong> {
                1401, 1402, 1403, 1404, 1419, 1420, 1421, 1422, 1424, 1425,
                1426, 1427, 1431, 149905, 149907, 31901
            }},
            { "Heavy Guardian", new List<ulong> {
                1907, 1924, 1925, 1927, 1937, 50049, 5033
            }},
        };

        public static string GetNpcBossName(ulong npcId) => npcId switch
        {
            86 => "Sanctuary Flying Fish",
            87 => "Lizardman King",
            15395 => "Thunder Ogre",
            15202 => "Flame Ogre",
            15179 => "Frost Ogre",
            15323 => "Muk Chieftain",
            15269 => "Bandit Chieftain",
            2052 => "Bandit Chieftain's Battleaxe",
            15159 => "Savage Goldfang",
            1 => "(Boss) Savage Goldfang",
            148 => "Ironfang",
            146 => "(Phantom Spider) Hurricane Goblin King", // Note: ID 146 also maps to "Hurricane Goblin King" and "Phantom Spider"
            40 => "Goblin King",
            19 => "Muk King",
            //146 => "Phantom Spider",
            147 => "Toxic Beehive",
            1985 => "(Wind Dragon) Igoreus",
            101716 => "(Boss Wild Boar King)",
            425 => "Tina, Void Wraith",
            185 => "Katagriff",
            103588 => "Denver",
            _ => string.Empty
        };


        private static readonly Dictionary<ulong, string> skillToProfession = new();

        static Common()
        {
            foreach (var kvp in professionSkills)
            {
                foreach (var skill in kvp.Value)
                {
                    if (!skillToProfession.TryAdd(skill, kvp.Key))
                    {
                        Console.WriteLine($"[Duplicate Skill] {skill} already mapped to {skillToProfession[skill]}, attempted to assign to {kvp.Key}");
                    }
                }
            }
        }

        public static string GetProfessionBySkill(ulong skillId)
        {
            if (skillToProfession.TryGetValue(skillId, out var profession))
            {
                return profession;
            }

            //Console.WriteLine($"[Unrecognized Skill] {skillId} not found in mapping!");
            return "";
        }


        /// <summary>
        /// Wrapper for HTTP GET requests.
        /// </summary>
        /// <param name="url">Request URL</param>
        /// <param name="queryParams">Query parameters</param>
        /// <param name="cookies">Optional cookie string</param>
        /// <returns>A JSON object parsed from the response</returns>
        public async static Task<JObject> RequestGet(string url, object? queryParams = null, string cookies = "", object? headers = null)
        {
            JObject data;

            try
            {
                var response = await url
                    .SetQueryParams(queryParams)
                    .GetAsync();

                // 获取响应的内容并解析为 JSON
                var result = await response.GetJsonAsync<JObject>();
                data = JObject.FromObject(result);
            }
            catch (HttpRequestException ex)
            {
                Console.WriteLine($"Error in HTTP request: {ex.Message}");
                data = JObject.FromObject(new { code = 401, error = ex.Message });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An unexpected error occurred: {ex.Message}");
                data = JObject.FromObject(new { code = 500, error = ex.Message });
            }

            return data;
        }

        /// <summary>
        /// Wrapper for HTTP POST requests.
        /// </summary>
        /// <param name="url">Request URL</param>
        /// <param name="queryParams">Payload object serialized to JSON</param>
        /// <param name="cookies">Optional cookie string</param>
        /// <returns>A JSON object parsed from the response</returns>
        public async static Task<JObject> RequestPost(string url, object queryParams, string cookies = "", object? headers = null)
        {
            JObject data;

            try
            {
                // 发送 POST 请求并接收 JSON 数据
                var result = await url
                    .WithCookies(cookies)
                    .WithHeaders(headers)
                    .PostJsonAsync(queryParams)
                    .ReceiveJson<JObject>();
                // 将 JSON 数据转换为 JObject

                data = JObject.FromObject(result);
            }
            catch (HttpRequestException ex)
            {
                Console.WriteLine($"Error in HTTP request: {ex.Message}");
                data = JObject.FromObject(new { code = 401, error = ex.Message });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An unexpected error occurred: {ex.Message}");
                data = JObject.FromObject(new { code = 500, error = ex.Message });
            }

            return data;
        }

        /// <summary>
        /// Resolve character nicknames by UID.
        /// </summary>
        /// <param name="uid"></param>
        /// <returns></returns>
        public async static Task<JObject> player_uid_map(List<string> uid)
        {
            string url = "https://api.jx3rec.com/player_uid_map";
            var query = new
            {
                uid = uid,

            };
            return await Common.RequestPost(url, query);


        }

        /// <summary>
        /// Generate a random token, typically used as a battle identifier.
        /// </summary>
        /// <param name="length"></param>
        /// <returns></returns>
        public static string GenerateToken(int length = 16)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
            var tokenChars = new char[length];

            using (var rng = RandomNumberGenerator.Create())
            {
                byte[] data = new byte[length];
                rng.GetBytes(data);

                for (int i = 0; i < length; i++)
                {
                    tokenChars[i] = chars[data[i] % chars.Length];
                }
            }

            return new string(tokenChars);
        }
        /// <summary>
        /// Format large numeric values using English units (K, M, B).
        /// </summary>
        public static string FormatWithEnglishUnits<T>(T number)
        {
            if (AppConfig.DamageDisplayType == 0)
            {
                double value = Convert.ToDouble(number);

                if (value < 10_000) // Leave as-is below 10k (use ToString("N0") for thousands separators if preferred)
                    return value % 1 == 0 ? ((long)value).ToString() : value.ToString("0.##");

                if (value >= 1_000_000_000) return (value / 1_000_000_000.0).ToString("0.##") + "B";
                if (value >= 1_000_000) return (value / 1_000_000.0).ToString("0.##") + "M";
                return (value / 1_000.0).ToString("0.##") + "K";
            }
            else
            {
                return FormatWithWanOnly(number);
            }

        }

        public static string FormatWithWanOnly<T>(T number, int maxDecimals = 2)
        {
            decimal v = Convert.ToDecimal(number);
            bool neg = v < 0;
            decimal abs = Math.Abs(v);

            string fmt(decimal x)
            {
                if (x == decimal.Truncate(x)) return decimal.Truncate(x).ToString();
                return x.ToString("0." + new string('#', Math.Max(0, maxDecimals)));
            }

            string core = abs < 10_000m ? fmt(abs) : fmt(abs / 10_000m) + "W";
            return neg ? "-" + core : core;
        }

        /// <summary>
        /// Legacy snapshot upload (deprecated and intentionally not implemented).
        /// </summary>
        public static async Task<bool> AddUserDps(BattleSnapshot snapshot)
        {
            throw new NotImplementedException("This feature is no longer available in the WinForm project.");

            //if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

            //// 1) 先从快照里找当前 UID
            //if (!snapshot.Players.TryGetValue(AppConfig.Uid, out var sp))
            //{
            //    // Snapshot does not contain the current player; nothing to upload.
            //    return false;
            //}

            //// 2) 基本信息（全部来自快照，保证与快照一致）
            //string nickName = sp.Nickname;
            //string professional = sp.Profession;
            //int combatPower = sp.CombatPower;
            //var subProfession = sp.SubProfession;
            //// 3) 伤害/治疗汇总（快照）
            //ulong totalDamage = sp.TotalDamage;

            //// 4) 实时秒伤 / 暴击率 / 幸运率 / 分伤 / 单次最大（快照优先，若快照未包含则用运行时兜底）
            //var runtime = StatisticData._manager.GetOrCreate(AppConfig.Uid);

            //double instantDps = sp.TotalDps;

            //int critRate = sp.CritRate > 0 ? (int)sp.CritRate : (int)runtime.DamageStats.GetCritRate();
            //int luckyRate = sp.LuckyRate > 0 ? (int)sp.LuckyRate : (int)runtime.DamageStats.GetLuckyRate();

            //double criticalDamage = sp.CriticalDamage > 0 ? sp.CriticalDamage : runtime.DamageStats.Critical;
            //double luckyDamage = sp.LuckyDamage > 0 ? sp.LuckyDamage : runtime.DamageStats.Lucky;
            //double critLuckyDamage = sp.CritLuckyDamage > 0 ? sp.CritLuckyDamage : runtime.DamageStats.CritLucky;

            //// NOTE: The original field name was maxInstantDps but it behaves more like “maximum single hit”.
            //double maxInstantDps = sp.MaxSingleHit > 0 ? sp.MaxSingleHit : runtime.DamageStats.MaxSingleHit;

            //// 5) Combat duration (format using your manager’s convention)
            //string duration = snapshot.Duration.TotalHours >= 1
            //    ? snapshot.Duration.ToString(@"hh\:mm\:ss")
            //    : snapshot.Duration.ToString(@"mm\:ss");



            //// 7) 技能列表（快照里的伤害技能汇总）
            //List<SkillSummary> kill = sp.DamageSkills ?? new List<SkillSummary>();

            //// 8) 组装并上报
            //string url = @$"{AppConfig.url}/add_user_dps";
            //var body = new
            //{
            //    uid = AppConfig.Uid,
            //    nickName,
            //    professional,
            //    combatPower,
            //    instantDps,
            //    totalDamage,
            //    critRate,
            //    luckyRate,
            //    criticalDamage,
            //    luckyDamage,
            //    critLuckyDamage,
            //    maxInstantDps,
            //    battleTime = duration,
            //    battleId = AppConfig.Uid,
            //    kill,
            //    subProfession
            //};

            //var resp = await RequestPost(url, body);
            //var code = resp["code"];

            //return code != null && code.ToString() == "200";
        }


        public static Image BytesToImage(byte[] bytes)
        {
            using var ms = new MemoryStream(bytes);
            using var img = Image.FromStream(ms); // Materialize the GDI+ object
            return new Bitmap(img);               // Clone to avoid tying the lifetime to the stream
        }

    }






}

