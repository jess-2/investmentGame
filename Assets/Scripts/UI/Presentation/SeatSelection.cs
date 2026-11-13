// NEXT-D04 기업별 정원 2칸 표시와 '후보 선택 → 취소/교체 → 확정' 기본안(Unity 의존 없음).
// - 칸 계산은 공개 remainingSeats와 본인 selectedCompanies만 쓴다. 타인 이름·주문은 쓰지 않는다.
// - 후보는 클라이언트 임시 상태다. 정원을 예약하지 않고, 확정 전까지 서버에 아무것도 보내지 않는다.
// - 서버가 차례를 넘기거나(selectionIndex/턴/단계 변경) 현재 차례 마감이 지나면 후보를 버린다.
// - 서버 확정 후 선택을 되돌리는 기능이 아니다(그건 A/B 규칙·계약 변경이 필요).
using System;

namespace BlackWhale.UI.Presentation
{
    public struct SeatSlots
    {
        public int Empty;              // 빈칸
        public int Mine;               // 내 확정
        public int OccupiedAnonymous;  // 다른 참가자가 선점(익명)

        public override string ToString() => "empty=" + Empty + " mine=" + Mine + " anon=" + OccupiedAnonymous;
    }

    public static class SeatMath
    {
        public const int Capacity = UiRules.CompaniesPerOrder; // 기업당 2명

        /// <summary>
        /// 내 확정 수 = 본인 선택에 있으면 1, 없으면 0
        /// 점유 총수 = 2 - remainingSeats
        /// 익명 선점 수 = 점유 총수 - 내 확정 수
        /// 빈칸 수 = remainingSeats
        /// 메시지 도착 순서가 잠깐 어긋나도 각 값은 0~2로 제한한다(다음 스냅샷에서 맞춰짐).
        /// </summary>
        public static SeatSlots Compute(int remainingSeats, bool ownSelected)
        {
            int empty = Clamp(remainingSeats);
            int occupied = Capacity - empty;
            int mine = ownSelected ? 1 : 0;
            if (mine > occupied) occupied = mine; // 개인 결과가 공개 상태보다 먼저 온 경우
            empty = Math.Min(empty, Capacity - occupied);
            int anon = Clamp(occupied - mine);
            return new SeatSlots { Empty = empty, Mine = mine, OccupiedAnonymous = anon };
        }

        private static int Clamp(int v) => v < 0 ? 0 : v > Capacity ? Capacity : v;
    }

    public enum CandidateBlock
    {
        None,
        NotMyTurn,
        NoCandidate,
        SeatFull,
        AlreadyMine,
        Pending,
        Expired
    }

    public sealed class SelectionCandidate
    {
        private int _turn = -1;
        private int _selectionIndex = -1;

        public string CompanyId { get; private set; }
        public bool HasCandidate => !string.IsNullOrEmpty(CompanyId);

        /// <summary>공개 상태가 바뀔 때마다 호출. 내 차례가 아니게 되거나 차례 번호가 바뀌면 후보를 버린다.</summary>
        /// <returns>후보가 버려졌으면 true</returns>
        public bool Sync(int turn, string phase, int selectionIndex, bool myTurn)
        {
            bool changed = turn != _turn || selectionIndex != _selectionIndex;
            _turn = turn;
            _selectionIndex = selectionIndex;
            bool drop = HasCandidate && (changed || !myTurn || phase != BlackWhale.Shared.Protocol.Selection);
            if (drop) CompanyId = null;
            return drop;
        }

        /// <summary>후보 지정. 같은 기업을 다시 누르면 해제, 다른 기업이면 교체. 서버 전송 없음.</summary>
        public void Toggle(string companyId)
        {
            CompanyId = CompanyId == companyId ? null : companyId;
        }

        public void Clear() => CompanyId = null;

        /// <summary>확정 가능 여부. remainingSeconds는 PhaseClock의 현재 차례 남은 시간(표시용 추정).</summary>
        public CandidateBlock CanConfirm(bool myTurn, int remainingSeats, bool alreadyMine, bool pending, double remainingSeconds)
        {
            if (!myTurn) return CandidateBlock.NotMyTurn;
            if (pending) return CandidateBlock.Pending;
            if (!HasCandidate) return CandidateBlock.NoCandidate;
            if (remainingSeconds <= 0) return CandidateBlock.Expired;
            if (alreadyMine) return CandidateBlock.AlreadyMine;
            if (remainingSeats <= 0) return CandidateBlock.SeatFull;
            return CandidateBlock.None;
        }

        public static string Explain(CandidateBlock block)
        {
            switch (block)
            {
                case CandidateBlock.NotMyTurn: return "지금은 내 차례가 아닙니다.";
                case CandidateBlock.NoCandidate: return "먼저 기업 카드를 눌러 후보를 고르세요.";
                case CandidateBlock.SeatFull: return "방금 정원이 찼습니다. 다른 기업을 고르세요.";
                case CandidateBlock.AlreadyMine: return "이미 선택한 기업입니다.";
                case CandidateBlock.Pending: return "확정 처리 중입니다.";
                case CandidateBlock.Expired: return "차례 시간이 끝났습니다. 서버가 자동으로 선택합니다.";
                default: return "";
            }
        }
    }
}
