# rem-el

Repo cho EldoradoDesk — app desktop (.NET 8, WPF) quản lý shop Eldorado.gg của gia3010q,
kèm "bộ não" tự động báo giá của Rem.

## Có gì trong này

| Thư mục / File | Nội dung |
|---|---|
| `ui-fix/MainWindow.xaml` | Giao diện mới (viết lại toàn bộ): mô tả yêu cầu hiện ngay trong danh sách + panel chi tiết, trạng thái đơn dạng pill màu, thẻ card tối, form báo giá gọn. Giữ nguyên tên control và hàm xử lý nên code-behind cũ biên dịch được ngay. |
| `ui-fix/PATCH.md` | Hướng dẫn chi tiết từng bước: sửa lỗi mô tả, quét 15 giây, tích hợp Rem tự nhận đơn (kèm code mẫu copy-paste được). |
| `ui-fix/QuoteEngine.cs` | Engine tự động báo giá: đọc `prices.json`, định giá yêu cầu theo đúng bảng giá shop (bundle cộng giá + cộng thời gian, fragments $1/20k, level max $2.50, secret quest $2.50, Steal an Egg treo $2/ngày). Chỉ báo giá khi khớp chắc chắn, còn lại trả lý do để người bán quyết. |
| `ui-fix/prices.json` | Bảng giá 63 món trong shop + từ khóa nhận diện. Đổi giá trong file này là engine áp dụng ngay, không cần build lại. |

## Cách áp dụng vào app thật

Xem `ui-fix/PATCH.md` mục 4 (áp patch) — tóm tắt: đè `MainWindow.xaml` vào
project `Eldorado.Desktop`, thêm `QuoteEngine.cs` + `prices.json` vào project
`Eldorado.Core`, áp patch code, rồi build (Visual Studio hoặc push lên repo
`appel` để GitHub Actions build).

## Ranh giới an toàn

- App **tự gửi báo giá** khi khớp chắc chắn với bảng giá.
- App **không bao giờ tự xác nhận giao hàng** — giao hàng chỉ khi người bán bấm nút sau khi cày xong trong game.
- Khi app tự báo giá thì tắt watcher trình duyệt của Rem để không gửi trùng báo giá.
