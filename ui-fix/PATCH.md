# EldoradoDesk — Sửa lỗi mô tả + Giao diện mới

Ngày: 2026-11-06 · Bản app gốc: v1.0.0 (commit `7376e37d`) · File giao diện mới: `MainWindow.xaml` (thay thế toàn bộ file cũ)

## 1. Lỗi không hiện mô tả — nguyên nhân gốc

Mổ xẻ code đã decompile (`Eldorado.Core.dll` + `Eldorado.Desktop.dll`) thấy lỗi ở **3 tầng, tầng nào cũng thiếu mô tả**:

| Tầng | Vấn đề |
|---|---|
| **API (`SellerApi.GetRequestsAsync`)** | Chỉ đọc 5 trường từ `api/boostingOffers/me/boostingRequests/received`: `id`, `gameId`, `boostingCategoryTitle`, `buyerUsername`, `createdDate`. Không đọc mô tả — và endpoint này theo Swagger cũng **không trả mô tả** (chỉ có id, game, category, ngày tạo, người mua). |
| **Model (`BoostRequest`)** | Record chỉ có `(Id, Game, Category, Buyer, Created)` — không có chỗ nào chứa mô tả, nên UI không có gì để hiện. |
| **UI (`MainWindow.RequestSelected`)** | Panel chi tiết chỉ ghép `Category + Game + Buyer + Id + Created`. Không có dòng nào cho mô tả. |

**Mô tả nằm ở đâu?** Ở endpoint thông báo `GET /api/notifications/me`: mỗi thông báo có object `details` chứa `title`, `text`/`description` và `detailsId` (= đúng id của yêu cầu). Cách sửa: khi tải yêu cầu, tải kèm thông báo rồi **ghép theo `detailsId`**.

## 2. Patch code

### 2.1 `Eldorado.Core` — thêm Title + Description vào model

Tìm dòng:
```csharp
public sealed record BoostRequest(string Id, string Game, string Category, string Buyer, DateTimeOffset Created);
```
Thay bằng:
```csharp
public sealed record BoostRequest(string Id, string Game, string Category, string Buyer, DateTimeOffset Created, string Title, string Description);
```

### 2.2 `Eldorado.Core` — `GetRequestsAsync` ghép mô tả từ thông báo

Tìm method `GetRequestsAsync` cũ, thay toàn bộ bằng:
```csharp
public async Task<List<BoostRequest>> GetRequestsAsync()
{
    List<JsonElement> items = Results(await SendAsync(HttpMethod.Get, "api/boostingOffers/me/boostingRequests/received?filter=ActiveRequests&pageSize=50")).ToList();
    Dictionary<string, (string Title, string Description)> notes = await GetNotificationDetailsAsync();
    return items.Select(x =>
    {
        string id = Required(x, "id");
        // Phòng trường hợp endpoint nhận yêu cầu sau này trả thêm title/description thì đọc luôn.
        string title = Text(x, "title", Text(x, "boostingCategoryTitle"));
        string description = Text(x, "description", Text(x, "details", Text(x, "text")));
        if (notes.TryGetValue(id, out var n))
        {
            if (title.Length == 0 || title == Text(x, "boostingCategoryTitle"))
            {
                title = n.Title.Length > 0 ? n.Title : title;
            }
            if (description.Length == 0)
            {
                description = n.Description;
            }
        }
        return new BoostRequest(id, Text(x, "gameId"), Text(x, "boostingCategoryTitle"), Text(x, "buyerUsername"), Date(x, "createdDate") ?? DateTimeOffset.MinValue, title, description);
    }).ToList();
}

private async Task<Dictionary<string, (string Title, string Description)>> GetNotificationDetailsAsync()
{
    var map = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
    try
    {
        foreach (JsonElement n in Results(await SendAsync(HttpMethod.Get, "api/notifications/me")))
        {
            // Thử đọc object "details" trước, rồi fallback sang các trường ở chính notification.
            JsonElement d = Field(n, "details");
            string id = Text(d, "detailsId", Text(n, "detailsId"));
            if (id.Length == 0)
            {
                continue;
            }
            string title = Text(d, "title", Text(n, "title"));
            string description = Text(d, "text", Text(d, "description", Text(n, "text", Text(n, "description"))));
            map[id] = (title, description);
        }
    }
    catch (ApiException)
    {
        // Thông báo tải lỗi thì bỏ qua, không chặn danh sách yêu cầu.
    }
    return map;
}
```

> Lưu ý: nếu Swagger trả tên trường khác (`detailsId`/`title`/`text`), chỉ cần sửa tên trường trong `GetNotificationDetailsAsync` cho khớp. Logic ghép không đổi.

### 2.3 `Eldorado.Desktop` — `RequestSelected` hiện mô tả lên đầu panel chi tiết

Tìm dòng gán `RequestDetails.Text` trong `RequestSelected`, thay bằng:
```csharp
string description = string.IsNullOrWhiteSpace(boostRequest.Description)
    ? "Khách chưa ghi mô tả, hoặc mô tả không có trong thông báo. Mở yêu cầu trên website Eldorado để đọc đầy đủ."
    : boostRequest.Description;
RequestDetails.Text = ((object)boostRequest == null) ? "Chọn yêu cầu của khách." : $"{boostRequest.Title}\n\n{description}\n\nDanh mục: {boostRequest.Category}\nGame: {boostRequest.Game}\nNgười mua: {boostRequest.Buyer}\n\nMã yêu cầu: {boostRequest.Id}\nNgày tạo: {boostRequest.Created.ToLocalTime():dd/MM/yyyy HH:mm}";
```

### 2.4 `Eldorado.Desktop` — (khuyến nghị) `CopyQuoteClick` copy kèm mô tả

Trong chuỗi `Clipboard.SetText`, thêm tiêu đề + mô tả vào sau dòng yêu cầu để khi dán ra ngoài vẫn đủ nội dung:
```csharp
System.Windows.Clipboard.SetText($"{boostRequest.Title}\n{boostRequest.Description}\nYêu cầu {quoteDraft.RequestId}\n{boostRequest.Category} · Game {boostRequest.Game}\nNgười mua {boostRequest.Buyer}\nGiá {quoteDraft.Price.ToString(CultureInfo.InvariantCulture)} {quoteDraft.Currency}\nThời gian {quoteDraft.DeliveryTime}");
```

## 3. Giao diện mới — `MainWindow.xaml` (file đính kèm, thay thế toàn bộ)

Giữ nguyên **tên control và tên hàm xử lý sự kiện** nên code-behind cũ biên dịch được ngay, chỉ cần patch mục 2.3.

Thay đổi chính so với bản cũ:
- **Mô tả hiện ngay trong danh sách:** cột "Yêu cầu của khách" ở trang Cày thuê hiển thị tiêu đề (đậm) + mô tả (xám, tối đa 2 dòng) cho từng yêu cầu, không cần bấm vào mới thấy.
- **Panel chi tiết có cấu trúc:** thẻ card bo tròn, mô tả nằm trên cùng, thông tin còn lại theo từng dòng có nhãn rõ ràng (trước đây là một khối text với dòng trống, khó đọc).
- **Trạng thái đơn bằng "pill" màu:** Paid = xanh lá, Delivered = xanh dương, Canceled = đỏ — thay vì chữ trơn.
- **Hệ thống thẻ + khoảng cách:** sidebar tối riêng, header lớn, nút chính vàng `#F7C948` (giữ màu accent gốc của app), nút phụ viền, ô nhập có viền focus vàng, DataGrid tối (header xám đậm, dòng xen kẽ, hover, dòng được chọn nổi bật).
- **Form báo giá gọn lại:** giá + tiền tệ cạnh nhau, nút Lưu nháp / Sao chép / Gửi báo giá xếp theo tầng rõ ràng.
- **Trang Cài đặt:** gom vào thẻ giữa màn hình thay vì dàn trải.
- Thanh trạng thái dưới cùng giữ nguyên.

## 4. Cách áp dụng

1. Copy `MainWindow.xaml` mới đè lên `Eldorado.Desktop/MainWindow.xaml` trong repo.
2. Thêm 2 file mới vào project: `QuoteEngine.cs` + `prices.json` (đặt `prices.json` cạnh file `.exe` khi build — để `Copy to Output Directory: Copy if newer`). Thêm `QuoteEngine.cs` vào project `Eldorado.Core`.
3. Áp patch 2.1 → 2.4 và mục 6, 7 ở dưới.
4. Build lại: mở bằng Visual Studio, hoặc push lên GitHub — workflow "Verify Windows compilation before packaging" sẽ build ra artifact mới.
5. Cài bản mới trên máy Windows và kiểm tra:
   - Trang Cày thuê: mỗi yêu cầu hiện tiêu đề + mô tả ngay trong danh sách.
   - Bấm vào yêu cầu: panel phải hiện mô tả đầy đủ ở trên cùng.
   - Gửi báo giá, sửa giá/số lượng offer, xác nhận giao hàng vẫn chạy như cũ.
   - Nếu một yêu cầu hiện dòng "mở yêu cầu trên website để đọc đầy đủ" nghĩa là nó không còn trong danh sách thông báo (quá cũ) — đó là giới hạn của API, không phải lỗi UI.
   - Bật "Tự động gửi báo giá" ở trang Kết nối shop, dán Discord webhook rồi tạo một yêu cầu thử: app phải tự báo giá trong vòng 15 giây và nhắn Discord.

## 5. Giới hạn đã biết

- Mô tả lấy từ thông báo nên **chỉ có với yêu cầu còn trong danh sách thông báo gần đây**. Yêu cầu cũ hơn sẽ hiện câu fallback ở mục 2.3. Muốn mô tả đầy đủ 100% thì phải đọc trang web của từng yêu cầu (API chính thức không có endpoint lấy 1 yêu cầu).
- Không đổi logic giá, tiền tệ, thời gian giao, hay cơ chế chống gửi trùng — chỉ thêm mô tả và làm lại giao diện.

## 6. Quét nhanh mỗi 15 giây (thêm ngày 2026-11-06)

Mục tiêu: đơn hàng và yêu cầu mới hiện lên app trong vòng 15 giây. Offer ít thay đổi nên vẫn quét mỗi 60 giây — làm vậy để không bị Eldorado giới hạn lượt gọi (mỗi lần quét offer gọi tới 6 endpoint, quét cả 3 thứ mỗi 15 giây là ~4 lần số lượt gọi cũ).

### 6.1 `MainWindow` — đổi chu kỳ timer

Tìm:
```csharp
private readonly DispatcherTimer pollTimer = new DispatcherTimer
{
    Interval = TimeSpan.FromSeconds(60.0)
};
```
Thay `60.0` bằng `15.0`.

Thêm field đếm nhịp (đặt cạnh `failedPolls`):
```csharp
private int pollTicks;
```

Đổi handler của timer (trong constructor):
```csharp
pollTimer.Tick += async delegate
{
    await RunAsync(SyncAllAsync);
};
```
thành:
```csharp
pollTimer.Tick += async delegate
{
    await RunAsync(PollTickAsync);
};
```

Thêm method mới (đặt cạnh `SyncAllAsync`):
```csharp
// Nhịp 15 giây: đơn + yêu cầu luôn quét; offer chỉ quét mỗi 4 nhịp (= 60 giây).
private async Task PollTickAsync()
{
    pollTicks++;
    await SyncAllAsync(includeOffers: pollTicks % 4 == 1);
}
```

### 6.2 `SyncAllAsync` — thêm tham số `includeOffers`

Đổi chữ ký:
```csharp
private async Task SyncAllAsync(bool includeOffers = true)
```
và chỉ tải offer khi `includeOffers` đúng:
```csharp
offers = await api.GetOffersAsync();
ApplyOfferFilter();
```
thành:
```csharp
if (includeOffers)
{
    offers = await api.GetOffersAsync();
    ApplyOfferFilter();
}
```

Trong `SyncAllAsync`, đổi 2 chỗ còn nhắc 60 giây:
```csharp
pollTimer.Interval = TimeSpan.FromSeconds(60.0);
```
thành `TimeSpan.FromSeconds(15.0)`, và:
```csharp
StatusText.Text = $"Đồng bộ lúc {DateTime.Now:HH:mm:ss} · {orders.Count} đơn · {requests.Count} yêu cầu · {offers.Count} offer · tự làm mới sau 60 giây";
```
thành `... tự làm mới sau 15 giây`.

Các chỗ khác **giữ nguyên**: nút Làm mới, xác nhận giao hàng, lưu giá/số lượng, gửi báo giá vẫn gọi `SyncAllAsync()` (đủ tham số mặc định → quét đầy đủ). Chế độ chờ khi lỗi cũng giữ nguyên (tối thiểu 60 giây khi API lỗi, 2 phút khi mất mạng) — đó là cơ chế chống spam khi Eldorado giới hạn, đừng hạ xuống 15 giây.

## 7. Tích hợp Rem: app tự nhận đơn (thêm ngày 2026-11-06)

Cách tích hợp: **đưa "bộ não" của Rem vào trong app** — app tự định giá và tự gửi báo giá theo đúng bảng giá shop của gia, rồi báo lại qua Discord. Rem (trợ lý chat) không thể chạy trực tiếp từ máy Windows của gia, nên app là nơi chạy logic đó; Rem vẫn là người giữ bảng giá và quyết các trường hợp khó.

Ranh giới an toàn (không đổi):
- ✅ App **tự gửi báo giá** khi khớp chắc chắn với bảng giá.
- ❌ App **không bao giờ tự xác nhận giao hàng** — giao hàng chỉ khi gia bấm nút sau khi cày xong trong game.
- ❌ Yêu cầu lạ, trade, mặc cả, món đã gỡ khỏi shop: app giữ lại để gia quyết định, không tự báo giá.

### 7.1 File mới — `QuoteEngine.cs` + `prices.json` (đã kèm trong thư mục này)

- `prices.json`: toàn bộ 63 món trong shop (giá + thời gian giao) kèm từ khóa để nhận diện tên món. Sửa giá trong file này là app áp dụng ngay, không cần build lại.
- `QuoteEngine.cs`: đọc `prices.json`, định giá một yêu cầu rồi trả về `QuoteResult`:
  - Khớp chắc → `CanAutoQuote = true` kèm giá (USD), thời gian giao, lý do.
  - Không khớp / cần người quyết → `CanAutoQuote = false` kèm lý do tiếng Việt.
  - Quy tắc đã mã hóa đúng bảng giá: bundle = cộng giá + cộng thời gian rồi làm tròn lên; fragments $1.00/20k; level max 2800→3000 $2.50/1 ngày; secret quest $2.50/1 ngày; Steal an Egg treo $2.00/ngày; bỏ qua Bounty Points, trade, món đã gỡ, yêu cầu mặc cả.

### 7.2 `MainWindow` — nối engine vào vòng quét

Thêm field (đặt cạnh `api`):
```csharp
private readonly QuoteEngine quotes = new QuoteEngine(Path.Combine(AppContext.BaseDirectory, "prices.json"));
private bool autoQuote = true;
private string? webhookUrl;
```

Thêm 2 control đã có sẵn trong `MainWindow.xaml` mới (trang Kết nối shop): `AutoQuoteCheck` (CheckBox), `WebhookInput` (TextBox), `SaveWebhookButton` (Button), `WebhookHint` (TextBlock). Thêm handler:
```csharp
private void AutoQuoteChanged(object sender, RoutedEventArgs e)
{
    autoQuote = AutoQuoteCheck.IsChecked == true;
    StatusText.Text = autoQuote ? "Đã bật tự động báo giá." : "Đã tắt tự động báo giá — yêu cầu mới chỉ hiện để bạn báo giá tay.";
}

private void SaveWebhookClick(object sender, RoutedEventArgs e)
{
    webhookUrl = WebhookInput.Text.Trim();
    WebhookHint.Text = string.IsNullOrEmpty(webhookUrl)
        ? "Đã xóa webhook — app sẽ chỉ báo bằng bong bóng ở khay hệ thống."
        : "Đã lưu webhook Discord. Đơn mới và báo giá đã gửi sẽ nhắn về Discord của bạn.";
}
```

Trong `SyncAllAsync`, sau khi tải xong `requests` (ngay sau dòng `ObserveNew("requests", ...)`), thêm:
```csharp
await AutoQuoteNewRequestsAsync(newEvents.Where(x => x.Stream == "requests").Select(x => x.Id).ToList());
```
và thêm 2 method mới (đặt cạnh `ShowNewNotification`):
```csharp
private async Task AutoQuoteNewRequestsAsync(List<string> newRequestIds)
{
    if (!autoQuote || api == null || newRequestIds.Count == 0)
    {
        return;
    }
    foreach (string id in newRequestIds)
    {
        BoostRequest? req = requests.FirstOrDefault(x => x.Id == id);
        if (req == null || store.GetStatus(id) is "Sending" or "Unknown" or "Sent" or "UserConfirmed")
        {
            continue;
        }
        QuoteResult q = quotes.Price(req);
        if (!q.CanAutoQuote)
        {
            await NotifyDiscordAsync($"Yêu cầu mới cần bạn xem: {req.Title}\nNgười mua: {req.Buyer}\nLý do chưa tự báo giá: {q.Reason}");
            continue;
        }
        try
        {
            store.SaveDraft(new QuoteDraft(id, q.Price, q.Currency, q.DeliveryTime, "Sending"));
            await api.SendQuoteAsync(req, q.Price, q.Currency, q.DeliveryTime);
            store.SetStatus(id, "Sent");
            await NotifyDiscordAsync($"Rem đã tự báo giá ${q.Price:G29} ({q.DeliveryTime}) cho {req.Buyer}: {req.Title}");
        }
        catch (ApiException)
        {
            store.SetStatus(id, "Unknown");
            await NotifyDiscordAsync($"Báo giá cho {req.Buyer} chưa xác định kết quả — mở Eldorado đối chiếu: {req.Title}");
            throw;
        }
    }
}

private async Task NotifyDiscordAsync(string message)
{
    if (string.IsNullOrWhiteSpace(webhookUrl))
    {
        return;
    }
    try
    {
        using var content = new System.Net.Http.StringContent(
            System.Text.Json.JsonSerializer.Serialize(new { content = message }),
            System.Text.Encoding.UTF8, "application/json");
        using HttpResponseMessage res = await http.PostAsync(webhookUrl, content);
        res.EnsureSuccessStatusCode();
    }
    catch
    {
        // Webhook lỗi thì bỏ qua, không chặn vòng quét.
    }
}
```

### 7.3 Lưu ý vận hành

- Khi app tự báo giá thì **tắt watcher trình duyệt của Rem** (2 cron đang tạm dừng) để không gửi trùng báo giá — 1 yêu cầu chỉ 1 bên xử lý.
- `prices.json` là nguồn giá của engine: gia đổi giá trong shop thì sửa luôn file này (hoặc nhắn Rem sửa giúp).
- Nếu Discord báo "giới hạn lượt gọi" nhiều, tăng chu kỳ ở mục 6 lên 30 giây.
- Khi nhận đơn mới (người mua đã chọn offer của gia), app vẫn chỉ **báo** cho gia — gia cày xong trong game rồi bấm "Xác nhận giao hàng" như cũ.
