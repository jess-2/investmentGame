// NEXT-D05~D06·IMP-07 화면용 표시 카탈로그(화면 객체 의존 없음).
// - 아이템·고유능력·입찰 종류·BOT 난이도의 '표시용' 이름·설명·분류. 규칙 문서 02(7·8·6장)와 05의 BOT 기본안을 요약했다.
// - 아직 A/B/C의 Shared 계약(명령·DTO·ID)이 없다. 여기의 ID 문자열은 D 화면 내부용 임시 키이며,
//   계약이 정해지면 그 ID로 매핑만 바꾼다(SHIELD는 02에 적힌 ID).
// - 실제 효과 계산·판정은 서버(B/C)가 한다. 이 파일은 설명 문구와 화면 분류만 갖는다.
using System.Collections.Generic;
using System.Linq;

namespace BlackWhale.UI.Presentation
{
    public enum ItemCategory
    {
        Finance,  // 금융형: 투자 결정 단계, 본인 선택 기업
        Info,     // 정보형: 정보 탐색 단계
        Attack,   // 공격형: 정보 탐색 단계, 다른 플레이어 대상
        Special   // 특수: 상점 없음(프리패스)
    }

    public enum ItemTarget
    {
        None,
        OwnCompany,    // 본인이 고른 기업 1개
        OwnInterval,   // 본인 주문의 구간 1개
        Company,       // 기업 1개(미열람 묶음이 있는)
        Player,        // 다른 플레이어 1명
        TopPlayer      // 턴 시작 총자산 상위 2명 중 본인 외 1명
    }

    public sealed class ItemDef
    {
        public string Id, Name, Short, Effect;
        public ItemCategory Category;
        public int PriceRateBps;   // 가격 = S × 비율. 300 = 3%
        public ItemTarget Target;
        public bool InShop => Category != ItemCategory.Special;
    }

    public static class ItemCatalog
    {
        public static readonly ItemDef[] All =
        {
            new ItemDef { Id = "AMPLIFIER", Name = "증폭기", Category = ItemCategory.Finance, PriceRateBps = 300, Target = ItemTarget.OwnCompany,
                Short = "선택 기업 손익률 ×2", Effect = "선택한 내 기업의 전체 구간 손익률을 2배로. 이익과 손실 모두 2배가 된다." },
            new ItemDef { Id = "CRUSHER", Name = "하락 파쇄기", Category = ItemCategory.Finance, PriceRateBps = 400, Target = ItemTarget.OwnCompany,
                Short = "선택 기업 손익률 부호 반전", Effect = "선택한 내 기업의 전체 구간 손익률 부호를 뒤집는다. 오른 기업에 쓰면 손실이 된다." },
            new ItemDef { Id = "SHIELD", Name = "방패", Category = ItemCategory.Finance, PriceRateBps = 200, Target = ItemTarget.OwnCompany,
                Short = "작은 손익 → +5%, 큰 손실 절반", Effect = "수익률이 -3%~+3%면 +5%로 바꾸고, -3% 미만이면 손실을 절반으로 줄인다. +3% 초과면 그대로." },
            new ItemDef { Id = "BIGBANG", Name = "대폭발 탐지기", Category = ItemCategory.Finance, PriceRateBps = 500, Target = ItemTarget.OwnInterval,
                Short = "구간 변화 15% 이상이면 +50%p", Effect = "내 주문의 구간 1개를 지정. 그 구간의 실제 변화 폭(절대값)이 15% 이상이면 그 기업 손익률에 +50%p." },
            new ItemDef { Id = "BLACKBOX", Name = "블랙박스 분석권", Category = ItemCategory.Info, PriceRateBps = 200, Target = ItemTarget.Company,
                Short = "미열람 1묶음 무료 공개", Effect = "선택한 기업의 아직 못 본 정보 1묶음을 무료로 연다. 열 묶음이 없으면 사용할 수 없다." },
            new ItemDef { Id = "DISTORT", Name = "정보 왜곡", Category = ItemCategory.Attack, PriceRateBps = 200, Target = ItemTarget.Player,
                Short = "상대 구매 정보 10초 가림", Effect = "대상이 코인으로 산 정보 중 1묶음을 10초 동안 ???로 가린다. 실제 값·정산은 바뀌지 않는다." },
            new ItemDef { Id = "STEAL", Name = "정보 훔치기", Category = ItemCategory.Attack, PriceRateBps = 300, Target = ItemTarget.Player,
                Short = "상대 구매 정보 1묶음 복사", Effect = "대상이 코인으로 산 정보 중 내가 못 본 1묶음을 서버가 골라 복사해 준다." },
            new ItemDef { Id = "DRAIN", Name = "수익률 감소", Category = ItemCategory.Attack, PriceRateBps = 400, Target = ItemTarget.TopPlayer,
                Short = "상위권 1명 투자금 10% 차감", Effect = "턴 시작 총자산 상위 2명 중 나를 뺀 1명을 지정. 정산 때 그 사람 투자금의 10%를 차감한다." },
            new ItemDef { Id = "FREEPASS", Name = "블랙웨일 프리패스", Category = ItemCategory.Special, PriceRateBps = 0, Target = ItemTarget.None,
                Short = "9턴 입찰 점수 +25%", Effect = "6턴 입찰로만 얻는다. 9턴 자리 입찰에서 사용하면 입찰 점수가 25% 올라간다(실제 비용은 그대로)." }
        };

        public static ItemDef Get(string id) => All.FirstOrDefault(i => i.Id == id);

        public static IEnumerable<ItemDef> Shop => All.Where(i => i.InShop);

        public static string CategoryName(ItemCategory c)
        {
            switch (c)
            {
                case ItemCategory.Finance: return "금융";
                case ItemCategory.Info: return "정보";
                case ItemCategory.Attack: return "공격";
                default: return "특수";
            }
        }

        /// <summary>사용 시점 안내.</summary>
        public static string WhenUsable(ItemCategory c)
        {
            switch (c)
            {
                case ItemCategory.Finance: return "투자 결정 단계 · 내 기업에";
                case ItemCategory.Info:
                case ItemCategory.Attack: return "정보 탐색 단계";
                default: return "9턴 입찰에서";
            }
        }
    }

    public sealed class AbilityDef
    {
        public string Id, Name, Short, Effect;
    }

    public static class AbilityCatalog
    {
        public static readonly AbilityDef[] All =
        {
            new AbilityDef { Id = "POLITICIAN", Name = "정치인", Short = "공격 1회 자동 방어",
                Effect = "들어오는 첫 공격 아이템이나 기업사냥꾼의 감산을 자동으로 막는다. 막은 뒤 3턴 후부터 다시 가능. 막으면 공격 아이템 1개를 받는다." },
            new AbilityDef { Id = "OPERATOR", Name = "작전 세력", Short = "정보 훔치기로 시작",
                Effect = "정보 훔치기 1개를 들고 시작. 수익률 감소로 실제 깎은 금액의 25%를 내 수익으로. 훔치기 성공 시 다음 턴 첫 정보 1묶음 무료." },
            new AbilityDef { Id = "INFOBROKER", Name = "정보상", Short = "매 턴 새 정보 5일 무료",
                Effect = "매 턴 새로 확인하는 정보 5일까지 무료로 구매한다. 블랙박스 분석권은 같은 기업의 미열람 정보를 연속 5일씩 최대 2회 연다." },
            new AbilityDef { Id = "RAIDER", Name = "기업사냥꾼", Short = "파쇄기 성공 시 상대 수익 감산",
                Effect = "하락 파쇄기로 떨어진 기업에서 이익을 내면, 같은 기업에서 이익을 본 상대 1명의 수익을 깎고 깎은 금액의 50%를 가져온다." },
            new AbilityDef { Id = "GAMBLER", Name = "도박사", Short = "고변동 기업 손익 ×1.2",
                Effect = "AI·SPACE·BIO 투자의 수익률과 손실률이 1.2배. 증폭기와 함께면 2.4배, 대폭발 보너스는 +60%p." }
        };

        public static AbilityDef Get(string id) => All.FirstOrDefault(a => a.Id == id);
    }

    public enum AuctionKind
    {
        BlackBox,      // 3턴: 블랙박스 분석권 1개
        FreePass,      // 6턴: 블랙웨일 프리패스 1개
        SeatPriority   // 9턴: 9·10턴 자리 선택 순위
    }

    public static class AuctionCatalog
    {
        public static AuctionKind? KindOf(int turn)
            => turn == 3 ? AuctionKind.BlackBox : turn == 6 ? AuctionKind.FreePass : turn == 9 ? AuctionKind.SeatPriority : (AuctionKind?)null;

        public static string Title(AuctionKind k)
            => k == AuctionKind.BlackBox ? "블랙박스 분석권" : k == AuctionKind.FreePass ? "블랙웨일 프리패스" : "9·10턴 자리 선택 순위";

        public static string Rule(AuctionKind k)
            => k == AuctionKind.SeatPriority
                ? "입찰액이 높은 순서로 원하는 자리(A~D)를 배정받습니다. 모두 자리를 받으므로 각자 자기 입찰액을 냅니다."
                : "가장 높게 부른 1명만 받고, 그 사람만 자기 입찰액을 냅니다. 모두 0이면 유찰입니다.";

        public static int TurnOf(AuctionKind k) => k == AuctionKind.BlackBox ? 3 : k == AuctionKind.FreePass ? 6 : 9;
    }

    public enum BotDifficulty
    {
        Easy,
        Normal,
        Hard
    }

    public static class BotDifficultyInfo
    {
        public static string Name(BotDifficulty d) => d == BotDifficulty.Easy ? "쉬움" : d == BotDifficulty.Normal ? "보통" : "어려움";

        public static string Describe(BotDifficulty d)
        {
            switch (d)
            {
                case BotDifficulty.Easy: return "정보를 턴당 최대 1묶음 산다. 알려진 값으로 오르는 구간 중 하나를 무작위로 고른다.";
                case BotDifficulty.Normal: return "정보를 턴당 최대 2묶음 산다. 알려진 값으로 가능한 구간을 모두 비교해 좋은 기업·구간을 고른다.";
                default: return "정보를 턴당 최대 3묶음 산다. 구간 1~2개와 투자 비율(5:5/6:4/7:3)까지 비교해 고른다.";
            }
        }
    }
}
