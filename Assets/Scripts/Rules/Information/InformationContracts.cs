using System;
using System.Collections.Generic;

namespace InvestmentGame.Rules.Information
{
    /// <summary>
    /// 정보 구매 기능에서 공통으로 사용하는 게임 규칙 상수 모음이다.
    /// 숫자를 여러 메서드에 직접 쓰지 않고 한 곳에 모아 두면 rules-v2의 수치가 바뀌었을 때
    /// 구매 비용, 묶음 범위, 턴별 지급량을 일관되게 수정할 수 있다.
    /// </summary>
    public static class InformationConstants
    {
        // 하나의 정보 묶음은 연속된 5일을 표현한다.
        public const int BundleSizeDays = 5;
        // 1턴의 30일 시장은 5일 묶음 6개로 나뉜다. 묶음 번호는 0부터 시작한다.
        public const int BundleCount = 6;
        // 정보 구매는 게임머니가 아니라 황금 코인 1개를 사용한다.
        public const int InformationCostCoins = 1;
        // 1~5턴 시작 시 지급하는 코인 수이다.
        public const int EarlyTurnCoinGrant = 3;
        public const int DailyEarlyTurnCoinGrant = 15;
        public const int DailyLateTurnCoinGrant = 25;
        // 6~10턴 시작 시 증가하는 코인 지급량이다.
        public const int LateTurnCoinGrant = 5;
        // 이 턴부터 후기 지급량을 적용한다.
        public const int LateGrantStartsAtTurn = 6;
    }

    /// <summary>
    /// C가 B의 시장 원본을 읽기 위한 최소 계약이다.
    /// C는 시장을 생성하거나 무료 묶음을 다시 추첨하지 않고, 이미 만들어진 값을 읽기만 한다.
    /// </summary>
    public interface IMarketInformationReader
    {
        // 특정 기업에서 전원에게 공개된 묶음 번호만 반환한다.
        IReadOnlyList<int> GetPublicBundleIndices(int turn, string companyId);
        // 구매 또는 개인뷰 구성에 필요한 하나의 묶음을 조회한다.
        bool TryGetBundle(int turn, string companyId, int bundleIndex, out MarketInformationBundle bundle);
    }

    /// <summary>
    /// B가 제공한 시장의 5일 정보 묶음이다.
    /// 생성 시 배열을 복사하여 B의 원본 배열과 C 또는 D가 받는 결과가 같은 참조를 공유하지 않게 한다.
    /// </summary>
    public sealed class MarketInformationBundle
    {
        /// <summary>기업, 0부터 시작하는 묶음 번호, 5일 변화율을 받아 불변에 가까운 값 객체를 만든다.</summary>
        public MarketInformationBundle(string companyId, int bundleIndex, IReadOnlyList<int> dailyChangesBps)
        {
            CompanyId = companyId;
            BundleIndex = bundleIndex;
            DailyChangesBps = Copy(dailyChangesBps);
        }

        public string CompanyId { get; }
        public int BundleIndex { get; }
        // 묶음 0은 1일차, 묶음 1은 6일차이므로 계산식은 bundleIndex * 5 + 1이다.
        public int StartDay => BundleIndex * InformationConstants.BundleSizeDays + 1;
        public IReadOnlyList<int> DailyChangesBps { get; }

        /// <summary>개인 응답이나 개인뷰에 넘길 때 원본 배열과 분리된 복사본을 만든다.</summary>
        public MarketInformationBundle CopyOf() => new MarketInformationBundle(CompanyId, BundleIndex, DailyChangesBps);

        /// <summary>읽기 전용 인터페이스만으로는 실제 배열 공유를 막을 수 없으므로 직접 배열을 복사한다.</summary>
        private static int[] Copy(IReadOnlyList<int> values)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            var copy = new int[values.Count];
            for (var i = 0; i < values.Count; i++) copy[i] = values[i];
            return copy;
        }
    }

    /// <summary>
    /// D가 만들고 A가 전달하는 정보 구매 요청의 도메인 표현이다.
    /// remainingCoins 같은 클라이언트 상태는 받지 않으며, 코인 잔액은 C의 서버 상태만 사용한다.
    /// </summary>
    public sealed class InformationRequest
    {
        public InformationRequest(string requestId, int turn, string companyId, int bundleIndex)
        {
            RequestId = requestId ?? string.Empty;
            Turn = turn;
            CompanyId = companyId ?? string.Empty;
            BundleIndex = bundleIndex;
        }

        public string RequestId { get; }
        public int Turn { get; }
        public string CompanyId { get; }
        public int BundleIndex { get; }
    }

    /// <summary>
    /// 정보 구매 결과이다.
    /// 성공 시 요청한 5일 값만 포함하고, 실패 시 미공개 시장 원본을 절대 포함하지 않는다.
    /// </summary>
    public sealed class InformationBuyResponse
    {
        /// <summary>생성자를 비공개로 두고 성공/실패 팩토리 메서드로 결과 형태를 명확히 한다.</summary>
        private InformationBuyResponse(bool success, string errorCode, int remainingCoins,
            string companyId, int bundleIndex, int startDay, IReadOnlyList<int> dailyChangesBps)
        {
            Success = success;
            ErrorCode = errorCode ?? string.Empty;
            RemainingCoins = remainingCoins;
            CompanyId = companyId ?? string.Empty;
            BundleIndex = bundleIndex;
            StartDay = startDay;
            DailyChangesBps = Copy(dailyChangesBps);
        }

        public bool Success { get; }
        public string ErrorCode { get; }
        public int RemainingCoins { get; }
        public string CompanyId { get; }
        public int BundleIndex { get; }
        public int StartDay { get; }
        public IReadOnlyList<int> DailyChangesBps { get; }

        /// <summary>구매가 실제로 확정된 경우에만 시장 묶음과 차감 후 잔액을 반환한다.</summary>
        public static InformationBuyResponse Succeeded(MarketInformationBundle bundle, int remainingCoins) =>
            new InformationBuyResponse(true, string.Empty, remainingCoins, bundle.CompanyId,
                bundle.BundleIndex, bundle.StartDay, bundle.DailyChangesBps);

        /// <summary>실패 응답은 오류 코드와 현재 잔액만 반환하여 비공개 시세가 노출되지 않게 한다.</summary>
        public static InformationBuyResponse Failed(string errorCode, int remainingCoins) =>
            new InformationBuyResponse(false, errorCode, remainingCoins, string.Empty, -1, 0, Array.Empty<int>());

        /// <summary>요청 캐시를 재전송할 때 응답 배열까지 새로 복사한다.</summary>
        public InformationBuyResponse CopyOf() =>
            new InformationBuyResponse(Success, ErrorCode, RemainingCoins, CompanyId, BundleIndex, StartDay, DailyChangesBps);

        /// <summary>실패 응답의 null 배열도 안전한 빈 배열로 바꾸고, 성공 배열은 깊은 복사한다.</summary>
        private static int[] Copy(IReadOnlyList<int> values)
        {
            if (values == null) return Array.Empty<int>();
            var copy = new int[values.Count];
            for (var i = 0; i < values.Count; i++) copy[i] = values[i];
            return copy;
        }
    }

    /// <summary>
    /// 플레이어 개인뷰에 표시할 수 있는 하나의 묶음이다.
    /// source로 PUBLIC과 PURCHASED를 구분하여 UI가 공개 범위와 구매 범위를 설명할 수 있게 한다.
    /// </summary>
    public sealed class VisibleInformationBundle
    {
        public VisibleInformationBundle(MarketInformationBundle bundle, string source)
        {
            BundleIndex = bundle.BundleIndex;
            StartDay = bundle.StartDay;
            Source = source ?? string.Empty;
            DailyChangesBps = Copy(bundle.DailyChangesBps);
        }

        public int BundleIndex { get; }
        public int StartDay { get; }
        public string Source { get; }
        public IReadOnlyList<int> DailyChangesBps { get; }

        private static int[] Copy(IReadOnlyList<int> values)
        {
            var copy = new int[values.Count];
            for (var i = 0; i < values.Count; i++) copy[i] = values[i];
            return copy;
        }
    }

    /// <summary>한 기업에 대해 현재 플레이어가 볼 수 있는 정보만 묶은 개인뷰 조각이다.</summary>
    public sealed class CompanyInformationView
    {
        public CompanyInformationView(string companyId, IReadOnlyList<VisibleInformationBundle> visibleBundles)
        {
            CompanyId = companyId;
            VisibleBundles = new List<VisibleInformationBundle>(visibleBundles).AsReadOnly();
        }

        public string CompanyId { get; }
        public IReadOnlyList<VisibleInformationBundle> VisibleBundles { get; }
    }

    /// <summary>
    /// 특정 플레이어·턴에 대한 정보 개인뷰이다.
    /// 모든 시장 원본이 아니라 공개 묶음과 해당 플레이어의 구매 묶음만 포함한다.
    /// </summary>
    public sealed class PlayerInformationView
    {
        public PlayerInformationView(int turn, int coins, IReadOnlyList<CompanyInformationView> companies)
        {
            Turn = turn;
            Coins = coins;
            Companies = new List<CompanyInformationView>(companies).AsReadOnly();
        }

        public int Turn { get; }
        public int Coins { get; }
        public IReadOnlyList<CompanyInformationView> Companies { get; }
    }

    /// <summary>
    /// 턴 코인 지급 결과이다.
    /// Granted가 false이면 같은 턴 지급 원장이 이미 있어 잔액을 다시 늘리지 않았다는 뜻이다.
    /// </summary>
    public sealed class CoinGrantResult
    {
        internal CoinGrantResult(string playerId, int turn, int coins, bool granted)
        {
            PlayerId = playerId;
            Turn = turn;
            Coins = coins;
            Granted = granted;
        }

        public string PlayerId { get; }
        public int Turn { get; }
        public int Coins { get; }
        public bool Granted { get; }
    }
}
