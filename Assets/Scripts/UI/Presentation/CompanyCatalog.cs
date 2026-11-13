// NEXT-D03 기업 표시용 카탈로그(Unity 의존 없음).
// 통신·규칙의 기업 ID(AI/SPACE/FOOD/BANK/BIO/AUTO)는 그대로 쓰고, 화면에만 이름·분야·이미지 경로를 붙인다.
// [임시 표시] 원문에 정식 회사명·이미지가 없어 AI·우주·식품·은행·바이오·자동차 계열의 임시 이름이다.
// 팀이 정식 이름/에셋을 정하면 이 표만 바꾼다. 이미지가 없으면 화면은 텍스트 배지로 대체한다.
// 표시용 에셋에는 시장 원본 값을 넣지 않는다.
using System.Collections.Generic;

namespace BlackWhale.UI.Presentation
{
    public sealed class CompanyEntry
    {
        public string Id;
        public string DisplayName;   // 화면 이름(임시)
        public string Sector;        // 분야
        public string Volatility;    // 변동성(00_rules.json의 volatility를 한국어로)
        public string Badge;         // 이미지가 없을 때 배지 글자
        public string LogoResource;  // Resources 경로(없으면 대체 표시)
        public string ColorHex;      // 배지·강조색

        public string Label => DisplayName + " (" + Id + ")";
    }

    public static class CompanyCatalog
    {
        public const string LogoFolder = "CompanyLogos/";

        private static readonly Dictionary<string, CompanyEntry> Entries = new Dictionary<string, CompanyEntry>
        {
            ["AI"] = new CompanyEntry { Id = "AI", DisplayName = "뉴럴코어", Sector = "인공지능", Volatility = "변동성 높음", Badge = "AI", LogoResource = LogoFolder + "AI", ColorHex = "7C3AED" },
            ["SPACE"] = new CompanyEntry { Id = "SPACE", DisplayName = "스텔라오빗", Sector = "우주항공", Volatility = "변동성 높음", Badge = "SP", LogoResource = LogoFolder + "SPACE", ColorHex = "0EA5E9" },
            ["FOOD"] = new CompanyEntry { Id = "FOOD", DisplayName = "그린테이블", Sector = "식품", Volatility = "변동성 낮음", Badge = "FD", LogoResource = LogoFolder + "FOOD", ColorHex = "F59E0B" },
            ["BANK"] = new CompanyEntry { Id = "BANK", DisplayName = "새벽은행", Sector = "은행", Volatility = "변동성 낮음", Badge = "BK", LogoResource = LogoFolder + "BANK", ColorHex = "2563EB" },
            ["BIO"] = new CompanyEntry { Id = "BIO", DisplayName = "헬릭스바이오", Sector = "바이오", Volatility = "변동성 극단적", Badge = "BI", LogoResource = LogoFolder + "BIO", ColorHex = "10B981" },
            ["AUTO"] = new CompanyEntry { Id = "AUTO", DisplayName = "모빌로드", Sector = "자동차", Volatility = "변동성 중간", Badge = "AU", LogoResource = LogoFolder + "AUTO", ColorHex = "EF4444" },
        };

        /// <summary>알 수 없는 ID도 화면이 멈추지 않도록 ID 그대로의 대체 항목을 돌려준다.</summary>
        public static CompanyEntry Get(string companyId)
        {
            if (companyId != null && Entries.TryGetValue(companyId, out var e)) return e;
            var id = companyId ?? "?";
            return new CompanyEntry
            {
                Id = id, DisplayName = id, Sector = "", Volatility = "", ColorHex = "6B7280", LogoResource = null,
                Badge = id.Length >= 2 ? id.Substring(0, 2) : id
            };
        }

        public static IEnumerable<string> Ids => UiRules.CompanyIds;
    }
}
