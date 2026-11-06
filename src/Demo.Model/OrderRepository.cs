namespace Demo.Model;

/// <summary>订单仓储抽象（真实项目里换成数据库/API）。</summary>
public interface IOrderRepository
{
    /// <summary>读取全部订单快照。</summary>
    IReadOnlyList<Order> GetAll();

    /// <summary>按订单号查找。</summary>
    Order? Find(string orderId);

    /// <summary>按订单号替换订单，不存在返回 false。</summary>
    bool TryUpdate(Order order);

    /// <summary>新增订单；订单号已存在时抛 <see cref="InvalidOperationException"/>。</summary>
    void Add(Order order);

    /// <summary>删除订单，不存在返回 false。</summary>
    bool Remove(string orderId);
}

/// <summary>内存订单仓储：线程安全，用于演示与测试。</summary>
public sealed class InMemoryOrderRepository : IOrderRepository
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Order> _orders = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>创建带演示数据的仓储。</summary>
    public InMemoryOrderRepository()
        : this(CreateSeedOrders())
    {
    }

    private InMemoryOrderRepository(IEnumerable<Order> seedOrders)
    {
        ArgumentNullException.ThrowIfNull(seedOrders);

        foreach (Order order in seedOrders)
        {
            _orders[order.Id] = order;
        }
    }

    /// <summary>
    /// 用指定初始数据创建仓储。
    /// <para>
    /// 注意：这里用静态工厂而不是公开构造函数，避免 DI 容器在多个构造函数中选择「最贪婪」的那个
    /// （<c>IEnumerable&lt;Order&gt;</c> 恰好是可解析的，会导致被注入空集合）。
    /// </para>
    /// </summary>
    public static InMemoryOrderRepository FromOrders(IEnumerable<Order> seedOrders) => new(seedOrders);

    /// <inheritdoc />
    public IReadOnlyList<Order> GetAll()
    {
        lock (_gate)
        {
            return _orders.Values
                .OrderBy(order => order.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    /// <inheritdoc />
    public Order? Find(string orderId)
    {
        if (string.IsNullOrWhiteSpace(orderId))
        {
            return null;
        }

        lock (_gate)
        {
            return _orders.TryGetValue(orderId, out Order? order) ? order : null;
        }
    }

    /// <inheritdoc />
    public bool TryUpdate(Order order)
    {
        ArgumentNullException.ThrowIfNull(order);

        lock (_gate)
        {
            if (!_orders.ContainsKey(order.Id))
            {
                return false;
            }

            _orders[order.Id] = order;
            return true;
        }
    }

    /// <inheritdoc />
    public void Add(Order order)
    {
        ArgumentNullException.ThrowIfNull(order);

        lock (_gate)
        {
            if (!_orders.TryAdd(order.Id, order))
            {
                throw new InvalidOperationException($"订单号 {order.Id} 已存在。");
            }
        }
    }

    /// <inheritdoc />
    public bool Remove(string orderId)
    {
        if (string.IsNullOrWhiteSpace(orderId))
        {
            return false;
        }

        lock (_gate)
        {
            return _orders.Remove(orderId);
        }
    }

    private static IEnumerable<Order> CreateSeedOrders()
    {
        DateTimeOffset now = DateTimeOffset.Now;

        yield return new Order
        {
            Id = "SO-1001",
            CustomerName = "云栖科技",
            Amount = 128_000m,
            Status = OrderStatus.Pending,
            CreatedAt = now.AddDays(-1),
            Remark = "企业版年度授权",
        };

        yield return new Order
        {
            Id = "SO-1002",
            CustomerName = "枫林设计",
            Amount = 32_500m,
            Status = OrderStatus.Paid,
            CreatedAt = now.AddDays(-3),
            Remark = "设计协作席位 20 个",
        };

        yield return new Order
        {
            Id = "SO-1003",
            CustomerName = "云栖科技",
            Amount = 8_800m,
            Status = OrderStatus.Shipped,
            CreatedAt = now.AddDays(-7),
            Remark = "增值服务包",
        };

        yield return new Order
        {
            Id = "SO-1004",
            CustomerName = "北辰物流",
            Amount = 56_000m,
            Status = OrderStatus.Completed,
            CreatedAt = now.AddDays(-15),
            Remark = "私有化部署",
        };

        yield return new Order
        {
            Id = "SO-1005",
            CustomerName = "枫林设计",
            Amount = 4_200m,
            Status = OrderStatus.Cancelled,
            CreatedAt = now.AddDays(-20),
            Remark = "重复下单，已取消",
        };
    }
}
