using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BlueProto;
using Google.Protobuf;

namespace StarResonanceDpsAnalysis.WinForm.Core.test
{
    public static class SocialPrinter
    {
        /// <summary>尝试解析并打印社交/团队数据；成功返回 true</summary>
        public static bool TryParseAndPrint(byte[] payload)
        {
            if (payload == null || payload.Length == 0) return false;

            SocialData social = null;

            // 1) 服务器返回的社交数据
            if (TryParse<GetSocialDataReply>(payload, out var reply) && reply.Data != null)
            {
                social = reply.Data;
                Console.WriteLine("[SocialPrinter] Parsed as GetSocialDataReply → SocialData");
            }
            // 2) 直接推的 SocialData
            else if (TryParse<SocialData>(payload, out var direct))
            {
                social = direct;
                Console.WriteLine("[SocialPrinter] Parsed as SocialData");
            }
            else
            {
                return false; // 不是社交相关
            }

            PrintSocial(social);
            return true;
        }

        private static void PrintSocial(SocialData social)
        {
            Console.WriteLine("====== SocialData ======");

            if (social.TeamData != null) PrintTeam(social.TeamData);
            else Console.WriteLine("[Team] No team data");

            if (social.UnionData != null) PrintUnion(social.UnionData);
            else Console.WriteLine("[Union] No guild/alliance data");

            if (social.CommunityData != null) PrintCommunity(social.CommunityData);
            else Console.WriteLine("[Community] No community/home data");

            Console.WriteLine("========================");
        }

        private static void PrintTeam(CharTeam team)
        {
            Console.WriteLine("---- [Team] Overview ----");
            Console.WriteLine($"TeamId         : {team.TeamId}");
            Console.WriteLine($"LeaderId       : {team.LeaderId}");
            Console.WriteLine($"TeamTargetId   : {team.TeamTargetId}");
            Console.WriteLine($"TeamNum        : {team.TeamNum}");
            Console.WriteLine($"IsMatching     : {team.IsMatching}");
            Console.WriteLine($"Version        : {team.CharTeamVersion}");

            if (team.CharIds?.Count > 0)
                Console.WriteLine("CharIds        : " + string.Join(", ", team.CharIds));

            if (team.TeamMemberData != null && team.TeamMemberData.Count > 0)
            {
                Console.WriteLine("---- [Team] Member Details ----");
                foreach (var kv in team.TeamMemberData.OrderBy(k => k.Key))
                {
                    var m = kv.Value;
                    Console.WriteLine($"[Member] CharId={m.CharId}  (Key={kv.Key})");
                    Console.WriteLine($"         EnterTime     : {m.EnterTime}");
                    Console.WriteLine($"         OnlineStatus  : {m.OnlineStatus}");
                    Console.WriteLine($"         SceneId       : {m.SceneId}");
                    Console.WriteLine($"         TalentId      : {m.TalentId}");
                    Console.WriteLine($"         VoiceIsOpen   : {m.VoiceIsOpen}");
                    Console.WriteLine($"         GroupId       : {m.GroupId}");
                    if (m.SocialData != null) Console.WriteLine("         [Member.SocialData] Present");
                }
            }
            else
            {
                Console.WriteLine("[Team] No member details");
            }
        }

        private static void PrintUnion(UnionData union)
        {
            Console.WriteLine("---- [Union] Overview ----");
            Console.WriteLine($"UnionId   : {union.UnionId}");
            Console.WriteLine($"Name      : {union.Name}");
            Console.WriteLine($"HuntRank  : {union.UnionHuntRank}");
        }

        private static void PrintCommunity(CommunityData com)
        {
            Console.WriteLine("---- [Community] Overview ----");
            Console.WriteLine($"CommunityId            : {com.CommunityId}");
            Console.WriteLine($"HomelandId             : {com.HomelandId}");
            Console.WriteLine($"CohabitantIds (count)  : {com.CohabitantIds?.Count ?? 0}");
            if (com.CohabitantIds?.Count > 0)
                Console.WriteLine("CohabitantIds          : " + string.Join(", ", com.CohabitantIds));
        }

        private static bool TryParse<T>(byte[] payload, out T msg) where T : class, IMessage<T>, new()
        {
            msg = default;
            try
            {
                msg = new MessageParser<T>(() => new T()).ParseFrom(payload);
                return true;
            }
            catch { return false; }
        }
    }
}
