namespace CatClawMusic.Core.ColorQuantize;

internal class PQueue<T>
{
    private readonly List<T> _data;
    private readonly IComparer<T> _comparer;

    public PQueue(IComparer<T> comparer)
    {
        _comparer = comparer ?? throw new ArgumentNullException(nameof(comparer));
        _data = new List<T>();
    }

    public void Push(T item)
    {
        // 1. 리스트의 맨 끝에 데이터 추가
        _data.Add(item);
        
        // 2. 추가된 요소가 올바른 위치를 찾을 때까지 위로 이동 (Sift Up)
        SiftUp(_data.Count - 1);
    }

    public T Pop()
    {
        if (_data.Count == 0)
        {
            throw new InvalidOperationException("Queue is empty.");
        }

        // 1. 루트 노드(가장 우선순위가 높은 항목)를 저장
        T itemToReturn = _data[0];
        int lastIndex = _data.Count - 1;

        // 2. 마지막 요소를 루트로 이동
        _data[0] = _data[lastIndex];
        
        // 3. 마지막 요소 제거
        _data.RemoveAt(lastIndex);

        // 4. 새로운 루트가 올바른 위치를 찾을 때까지 아래로 이동 (Sift Down)
        //    (큐에 요소가 남아있을 경우에만 수행)
        if (_data.Count > 0)
        {
            SiftDown(0);
        }

        return itemToReturn;
    }

    public int Size()
    {
        return _data.Count;
    }

    private void SiftUp(int childIndex)
    {
        while (childIndex > 0)
        {
            int parentIndex = (childIndex - 1) / 2;
            
            // 부모 <= 자식 상태를 만들어야함
            if (_comparer.Compare(_data[childIndex], _data[parentIndex]) >= 0)
            {
                break;
            }
            
            // 부모가 자식보다 크다면, 교환
            Swap(childIndex, parentIndex);
            childIndex = parentIndex;
        }
    }

    private void SiftDown(int parentIndex)
    {
        int count = _data.Count;
        while (true)
        {
            int leftChildIndex = 2 * parentIndex + 1;
            
            // leaf node
            if (leftChildIndex >= count)
            {
                break;
            }

            int rightChildIndex = leftChildIndex + 1;
            int smallerChildIndex = leftChildIndex;

            // 자식이 둘 다 있다면 더 작은 것을 선택해서 부모로 올리기
            if (rightChildIndex < count && _comparer.Compare(_data[rightChildIndex], _data[leftChildIndex]) < 0)
            {
                smallerChildIndex = rightChildIndex;
            }

            // 자식이 하나만 있다면, 부모 <= 자식 상태를 만들어야 함
            if (_comparer.Compare(_data[parentIndex], _data[smallerChildIndex]) <= 0)
            {
                break;
            }

            // 자식 중에서 제일 작은 값을 부모로 올리기
            Swap(parentIndex, smallerChildIndex);
            parentIndex = smallerChildIndex;
        }
    }

    private void Swap(int index1, int index2)
    {
        T temp = _data[index1];
        _data[index1] = _data[index2];
        _data[index2] = temp;
    }
}