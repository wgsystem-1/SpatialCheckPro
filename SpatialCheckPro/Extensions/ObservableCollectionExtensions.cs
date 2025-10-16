using System.Collections.ObjectModel;

namespace SpatialCheckPro.Extensions
{
    /// <summary>
    /// ObservableCollection 확장 메서드
    /// </summary>
    public static class ObservableCollectionExtensions
    {
        /// <summary>
        /// 여러 항목을 한번에 추가합니다
        /// </summary>
        /// <typeparam name="T">컬렉션 항목 타입</typeparam>
        /// <param name="collection">대상 컬렉션</param>
        /// <param name="items">추가할 항목들</param>
        public static void AddRange<T>(this ObservableCollection<T> collection, IEnumerable<T> items)
        {
            if (collection == null) throw new ArgumentNullException(nameof(collection));
            if (items == null) return;

            foreach (var item in items)
            {
                collection.Add(item);
            }
        }
    }
}
