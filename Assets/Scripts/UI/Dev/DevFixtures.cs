// [개발 전용] 실제 B에 전달할 고정 시장 입력 읽기.
// 에디터에서만 프로젝트 루트의 '개발 정리/임시 데이터 및 검증용'을 읽는다(Assets 밖이라 빌드에 포함되지 않음).
// 방장 원본 시장·정답 파일을 클라이언트 Resources/StreamingAssets에 넣지 않는다.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlackWhale.Rules;
using BlackWhale.UI.Presentation;
using UnityEngine;

namespace BlackWhale.UI.Dev
{
    public static class DevFixtures
    {
        /// <summary>fixture 기준 선택 순서(A_selection_replay). 봇이 가능하면 이 순서대로 고른다.</summary>
        public static readonly string[] ReplayPicks = { "AI", "SPACE", "BIO", "AI", "BIO", "AUTO", "BANK", "FOOD" };

        public static string FixtureDir
            => Path.Combine(Directory.GetParent(Application.dataPath).FullName, "개발 정리", "임시 데이터 및 검증용");

        /// <summary>서버용 시장 입력만 읽는다. 정산 결과는 실제 B가 계산한다.</summary>
        public static void Load(out MarketData market, out string source)
        {
#if UNITY_EDITOR
            try
            {
                market = JsonUtility.FromJson<MarketData>(File.ReadAllText(Path.Combine(FixtureDir, "B_market_server.json")));
                new MarketBook(market, new InvestmentRules());
                if (market.turn == 1)
                {
                    source = "fixture(basic-turn-v2)";
                    return;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[DevFixtures] fixture를 읽지 못해 임의 시장으로 시작합니다: " + e.Message);
            }
#endif
            market = Synthetic();
            source = "개발용 예제 시장";
        }

        private static MarketData Synthetic()
        {
            var companies = new List<CompanyMarketData>();
            int seed = 0;
            foreach (var id in UiRules.CompanyIds)
            {
                int cap = UiRules.AbsoluteChangeCapBps(id);
                var values = Enumerable.Range(0, UiRules.MaxDay + 1)
                    .Select(d => d == 0 ? 0 : (int)(Math.Sin(d * 0.35 + seed) * cap * 0.6)).ToArray();
                companies.Add(new CompanyMarketData { companyId = id, dailyChangesBps = values, publicBundleIndices = new[] { seed % 6, (seed + 3) % 6 } });
                seed++;
            }
            return new MarketData { turn = 1, companies = companies.ToArray() };
        }
    }
}
