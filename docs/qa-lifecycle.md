# Kết quả kiểm tra bản mở rộng

## Kết quả chính

**166 test đạt, 0 lỗi, 0 bỏ qua**, gồm **9 test SQL Server 2022 thật**. Build Release toàn solution đạt **0 warning, 0 error**. EF xác nhận model không còn thay đổi chưa có migration. Kiểm tra cú pháp JavaScript, Bash và `git diff --check` đều đạt.

Mốc trước đợt mở rộng là 107 test. Các test mới bao phủ báo giá/revision/duyệt/cọc/tiến độ custom, preorder không tạo kho giả, gateway intent và callback, refund attempt/đối soát, hết hạn đơn/khôi phục voucher, shipping quote gắn giỏ, refresh/reset token, outbox/lease, media, limiter và nâng cấp SQLite có dữ liệu. Có kiểm tra Staff không được gọi các nghiệp vụ tài chính gateway mới; reset hoặc đổi quyền Admin thu hồi phiên cũ; client CMS cũ không làm trọng lượng SKU quay về mặc định.

```bash
dotnet build HoaSap.sln -c Release
# HOASAP_TEST_SQLSERVER lấy từ secrets, chỉ trỏ SQL Server dùng thử có quyền tạo/xóa DB.
dotnet test HoaSap.sln -c Release --no-build
```

Không đặt `HOASAP_TEST_SQLSERVER` thì 9 test SQL được đánh dấu bỏ qua. Kết quả 166/166 nói trên đã có biến này và chạy trên instance local riêng, không truy cập dữ liệu thật.

## Dữ liệu cũ và phục hồi

Fixture SQLite được sinh từ schema commit `13d90e6`, chứa tài khoản, đơn, khoản tiền, tồn kho đang giữ và voucher đã dùng. Test nâng cấp kiểm tra dữ liệu cũ giữ nguyên, thêm các bảng/cột mới và chạy lại không thay đổi dữ liệu. Các case schema không tương thích, backup bắt buộc và rollback cũng được kiểm tra. SQL Server kiểm tra migration từ schema EF trước đó và nghiệp vụ transaction bằng database GUID riêng.

Drill backup/restore SQLite/media bằng dữ liệu mẫu đạt integrity check, đối chiếu hàng của Customers/Orders/Payments/Inventories/Vouchers và SHA-256 media. SQL Server restore/volume của hạ tầng triển khai **chưa được diễn tập**. Xem [kết quả drill](../deployment/results/backup-restore.json).

## Hai instance và giao diện

Smoke-load chạy hai process Release dùng chung SQL Server: **1.200/1.200 request trả 200**, concurrency 20, p95 **47,91 ms**, p99 **63,66 ms**. Kiểm tra qua hai process xác nhận auth/giỏ, checkout replay cùng id, hủy lặp không trả kho hai lần. Cặp limiter quota 5 trả đúng 5 thành công rồi 3 lần 429. Đây là tải ngắn cục bộ, không phải chứng nhận năng lực production. [Số liệu đầy đủ](../deployment/results/local-smoke.json).

Đã mở trình duyệt kiểm tra storefront `/shop/` tải catalog/workshop, đăng nhập và hiện tài khoản; CMS `/admin/` đăng nhập và tải danh sách workshop/các thao tác ca học. Đây là kiểm tra trình duyệt thủ công có giới hạn, không phải bộ E2E browser đầy đủ. Kịch bản k6 đã được bổ sung nhưng chưa chạy bài tải kéo dài.

## Cần làm trước khi bán thật

Chưa thu/hoàn tiền hoặc gửi email/SMS thật. Adapter hiện là HTTPS/HMAC tổng quát, chưa phải kết nối provider cụ thể. Cần chọn provider/bridge tương thích, cấp merchant/secrets, cấu hình URL HTTPS/SMTP/SMS, cân trọng lượng SKU, shared media/key ring và thử giao dịch sandbox đầu-cuối. Tạo vận đơn/tracking tự động theo hãng, S3/CDN và lịch backup hạ tầng không nằm trong triển khai hiện tại. Kết quả local không khẳng định CI đã xanh hoặc hệ thống đã sẵn sàng production. Xem [runbook](operations.md).
