// QuoteEngine — "bộ não báo giá" của Rem, chạy ngay trong app.
// Đọc bảng giá từ prices.json (cạnh file .exe) rồi định giá yêu cầu của khách.
// Chỉ tự động báo giá khi khớp chắc chắn; còn lại trả về lý do để người bán quyết định.
// Không bao giờ tự xác nhận giao hàng — giao hàng chỉ khi người bán bấm nút.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Eldorado.Core;

public sealed record QuoteResult(
    bool CanAutoQuote,
    decimal Price,
    string Currency,
    string DeliveryTime,
    string Reason,
    string MatchedSummary);

public sealed class QuoteEngine
{
    private sealed record PriceItem(string Name, decimal Price, string Delivery, int DeliveryMinutes, string[] Aliases);

    private static readonly (string Name, int Minutes)[] DeliveryOptions =
    {
        ("Minute5", 5), ("Minute20", 20), ("Hour1", 60), ("Hour2", 120),
        ("Hour3", 180), ("Hour5", 300), ("Hour8", 480), ("Hour12", 720),
        ("Day1", 1440), ("Day2", 2880), ("Day3", 4320), ("Day5", 7200),
        ("Day7", 10080), ("Day10", 14400), ("Day14", 20160), ("Day28", 40320),
        ("Day45", 64800), ("Day60", 86400), ("Day80", 115200), ("Day100", 144000)
    };

    private readonly List<PriceItem> items = new();
    private readonly string loadError = "";

    public QuoteEngine(string pricesPath)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(pricesPath));
            JsonElement root = doc.RootElement;
            foreach (JsonElement el in root.GetProperty("items").EnumerateArray())
            {
                string delivery = el.GetProperty("delivery").GetString() ?? "Hour1";
                items.Add(new PriceItem(
                    el.GetProperty("name").GetString() ?? "",
                    el.GetProperty("price").GetDecimal(),
                    delivery,
                    el.GetProperty("deliveryMinutes").GetInt32(),
                    el.GetProperty("aliases").EnumerateArray().Select(a => a.GetString() ?? "").ToArray()));
            }
        }
        catch (Exception ex)
        {
            loadError = "Chưa đọc được bảng giá prices.json: " + ex.Message;
        }
    }

    public QuoteResult Price(BoostRequest request)
    {
        if (loadError.Length > 0)
        {
            return new QuoteResult(false, 0, "USD", "", loadError, "");
        }

        string text = Normalize(request.Title + " " + request.Description + " " + request.Category);
        if (text.Length == 0)
        {
            return NeedsReview("Yêu cầu không có tiêu đề/mô tả để định giá. Mở trên website để đọc rồi báo giá tay.");
        }

        // --- Các trường hợp KHÔNG tự báo giá ---
        if (text.Contains("bounty"))
        {
            return Skip("Bounty Points không bán.");
        }
        if (Regex.IsMatch(text, @"\b(trade|swap|account|acc)\b"))
        {
            return Skip("Yêu cầu có trade/đổi tài khoản — không tự báo giá, để người bán quyết định.");
        }
        if (text.Contains("west dragon") || text.Contains("runic fiend") || text.Contains("hellfire")
            || text.Contains("icecream") || text.Contains("ice cream") || text.Contains("slayers 2"))
        {
            return Skip("Món này đã gỡ khỏi shop — không báo giá khi chưa xác nhận lại.");
        }
        if (text.Contains("already has") || text.Contains("already have") || text.Contains("i have") || text.Contains("i already"))
        {
            return NeedsReview("Khách ghi đã có sẵn một phần — cần đọc kỹ để không báo giá thừa.");
        }
        if (text.Contains("cheaper") || text.Contains("discount") || text.Contains("lower price") || text.Contains("gia re"))
        {
            return NeedsReview("Khách muốn giá rẻ hơn giá niêm yết — để người bán quyết định.");
        }

        var parts = new List<(string Label, decimal Price, int Minutes)>();

        // --- Fragments: $1.00 / 20k theo đúng tỉ giá (15k = $0.75), 1h cho mỗi 20k ---
        Match frag = Regex.Match(text, @"(\d+(?:[.,]\d+)?)\s*(k|m)?\s*fragments?");
        if (frag.Success)
        {
            decimal amount = decimal.Parse(frag.Groups[1].Value, CultureInfo.InvariantCulture);
            if (frag.Groups[2].Value == "k") amount *= 1000;
            if (frag.Groups[2].Value == "m") amount *= 1000000;
            decimal units = amount / 20000m;
            decimal fragPrice = Math.Round(units * 1.00m, 2);
            int fragMinutes = (int)Math.Ceiling(units) * 60;
            parts.Add(($"{amount:#,##0} fragments", fragPrice, fragMinutes));
        }

        // --- Cày level max 2800 -> 3000: $2.50 / 1 ngày ---
        if ((text.Contains("2800") && text.Contains("3000")) || text.Contains("max level") || text.Contains("level max"))
        {
            parts.Add(("Level max 2800 → 3000", 2.50m, 1440));
        }

        // --- Secret quest: $2.50 / 1 ngày ---
        if (text.Contains("secret quest"))
        {
            parts.Add(("Secret quest", 2.50m, 1440));
        }

        // --- Steal an Egg treo: $2.00 / ngày ---
        if (text.Contains("steal an egg") || text.Contains("steal a egg"))
        {
            if (text.Contains("treo") || text.Contains("afk") || text.Contains("hang"))
            {
                Match days = Regex.Match(text, @"(\d+)\s*days?");
                int n = days.Success ? int.Parse(days.Groups[1].Value) : 1;
                parts.Add(($"Steal an Egg treo x{n} ngày", 2.00m * n, 1440 * n));
            }
            else
            {
                return Skip("Steal an Egg nhưng không nói treo — không rõ dịch vụ, để người bán quyết định.");
            }
        }

        // --- Khớp món trong bảng giá (tên dài khớp trước để tránh nhầm, vd Dragon Heart vs Dragon Storm) ---
        var matched = new List<(PriceItem Item, string Alias)>();
        foreach (PriceItem item in items)
        {
            string? best = item.Aliases.Where(a => a.Length > 0 && text.Contains(a)).OrderByDescending(a => a.Length).FirstOrDefault();
            if (best != null)
            {
                matched.Add((item, best));
            }
        }
        // Bỏ món khớp ngắn hơn khi từ khóa của nó nằm trong từ khóa dài hơn của món khác
        // (vd "trial v4" nằm trong "trial v4 duo" -> chỉ giữ Duo).
        matched = matched.Where(m => !matched.Any(o => o.Item != m.Item && o.Alias.Length > m.Alias.Length && o.Alias.Contains(m.Alias))).ToList();
        // Fragment đã tính theo tỉ giá thì không cộng thêm món "Get 15k fragment" trong shop.
        if (frag.Success)
        {
            matched.RemoveAll(m => m.Item.Name.Contains("fragment", StringComparison.OrdinalIgnoreCase));
        }
        foreach ((PriceItem item, _) in matched)
        {
            parts.Add((item.Name, item.Price, item.DeliveryMinutes));
        }

        if (parts.Count == 0)
        {
            return NeedsReview("Không khớp món nào trong bảng giá — để người bán đọc và báo giá tay.");
        }

        // --- Tổng bundle: giá cộng dồn, thời gian cộng dồn rồi làm tròn lên ---
        decimal totalPrice = parts.Sum(p => p.Price);
        int totalMinutes = parts.Sum(p => p.Minutes);
        string delivery = RoundUpDelivery(totalMinutes, out bool capped);

        // Số lượng "x2/x3" khi chỉ có đúng 1 món: nhân giá (giao vẫn giữ nguyên thời gian niêm yết).
        if (matched.Count == 1 && parts.Count == 1)
        {
            Match qty = Regex.Match(text, @"\bx(\d+)\b");
            if (qty.Success && int.TryParse(qty.Groups[1].Value, out int n) && n > 1 && n <= 99)
            {
                totalPrice *= n;
            }
        }

        string summary = string.Join(" + ", parts.Select(p => $"{p.Label} (${p.Price:G29})"));
        string reason = capped
            ? $"Tự động báo giá: {summary} = ${totalPrice:G29}, giao {delivery} (đã chạm mức tối đa của form)."
            : $"Tự động báo giá: {summary} = ${totalPrice:G29}, giao {delivery}.";
        return new QuoteResult(true, totalPrice, "USD", delivery, reason, summary);
    }

    private static QuoteResult Skip(string reason) =>
        new(false, 0, "USD", "", reason, "");

    private static QuoteResult NeedsReview(string reason) =>
        new(false, 0, "USD", "", reason, "");

    private static string RoundUpDelivery(int minutes, out bool capped)
    {
        capped = false;
        foreach ((string name, int m) in DeliveryOptions)
        {
            if (minutes <= m)
            {
                return name;
            }
        }
        capped = true;
        return "Day100";
    }

    private static string Normalize(string value)
    {
        string s = value.ToLowerInvariant();
        s = Regex.Replace(s, @"[^a-z0-9 ]", " ");
        s = Regex.Replace(s, @"\s+", " ").Trim();
        return s;
    }
}
