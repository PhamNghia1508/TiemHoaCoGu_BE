# Vận hành cửa hàng, thanh toán và thông báo

## Trước khi bán hàng thật

Bản mở rộng có storefront `/shop/`, CMS `/admin/`, quy trình hoa đặt riêng, sổ thanh toán/hoàn tiền và các adapter có kiểm thử. **Chưa có kết nối thật tới VNPay, MoMo, ngân hàng, GHN/GHTK hoặc nhà cung cấp SMS nào.** Adapter hiện tại là hợp đồng HTTPS/HMAC tổng quát: cần một dịch vụ tương thích hoặc triển khai `IPaymentGateway`/`ICarrierAdapter` cho nhà cung cấp đã chọn. Không chỉ điền URL của một nhà cung cấp bất kỳ rồi coi như tích hợp xong.

Trước go-live phải cấu hình môi trường staging, callback HTTPS, secrets của nhà cung cấp, SMTP/SMS, trọng lượng đóng gói thực tế của từng SKU, shared storage, backup và kiểm tra giao dịch sandbox đầu-cuối. Không có giao dịch thu/hoàn tiền thật hoặc tin nhắn thật nào được gửi trong QA của bản này.

## Thanh toán và hoàn tiền

Cấu hình qua secrets/biến môi trường, không sửa khóa thật vào Git:

```text
Payments__Gateway__Endpoint=https://bridge.example.test/payment/
Payments__Gateway__MerchantId=<merchant>
Payments__Gateway__Secret=<secret-ngau-nhien>
Payments__Gateway__TimeoutSeconds=15
Payments__Gateway__AllowedReturnOrigins__0=https://shop.example.test
```

`Endpoint` là URL base của dịch vụ **tương thích hợp đồng trong `CommerceExtensions.cs`**. Adapter gọi `charge`/`refund`, gửi JSON cùng `X-Merchant-Id`, `X-Timestamp` (Unix seconds) và `X-Signature = HMAC-SHA256(secret, timestamp + "." + rawBody)`, chữ hex thường. HTTP redirect không được tự theo. Callback `POST /api/gateway/webhook` dùng chữ ký trên đúng raw body, kiểm tra lệch giờ tối đa 5 phút và chống xử lý lặp. Return URL phải HTTPS và thuộc allowlist; trang return không phải bằng chứng đã nhận tiền.

Khách dùng `GET /api/gateway/orders/{id}/balance`, sau đó `POST /api/gateway/orders/{id}/charge` với `{amount,returnUrl,idempotencyKey}`. Lần đầu của đơn custom phải đủ cọc, không vượt số tiền còn lại. Giữ nguyên key khi mạng lỗi. Intent được lưu trước khi gọi nhà cung cấp; timeout thành `Unknown`, **không tự gửi lệnh thu lại**. Khoản `Pending` không tính là tiền đã thu.

Admin xem `GET /api/gateway/reconciliation`; sau khi kiểm tra chứng từ độc lập, ghi `POST /api/gateway/reconciliation/{id}` với `outcome` là `paid` hoặc `not-charged`, `transactionCode` nếu đã thu, và `evidenceReference` là mã ticket/chứng từ không chứa dữ liệu cá nhân. Endpoint chỉ đối soát sổ, không gọi nhà cung cấp. Tiền đến sau khi đơn `Expired`/`Cancelled` không hồi sinh đơn hoặc giữ kho lại; đánh dấu `RefundRequired` và đi qua quy trình hoàn tiền.

Hoàn đơn: nhân viên tạo `/api/refunds/order/{orderId}`, Admin duyệt `/api/refunds/{id}/approve`. Admin có thể ghi nhận một khoản hoàn đã thực hiện ngoài hệ thống bằng `/process`, hoặc gọi `/process-gateway` với `originalTransactionCode`. Mỗi refund có attempt bền vững; cùng refund không được chuyển tiền lần hai. Timeout/biên nhận chưa rõ giữ trạng thái chờ đối soát; `/reconcile-gateway` chỉ ghi mã biên nhận đã xác minh, không gửi lại lệnh hoàn. Staff không được duyệt, xử lý hoặc đối soát hoàn đơn/gateway.

## Vận chuyển

```text
Shipping__Carrier__Endpoint=https://bridge.example.test/shipping/
Shipping__Carrier__MerchantId=<merchant>
Shipping__Carrier__Secret=<secret>
Shipping__Carrier__Origin=<dia-chi-kho>
Shipping__Carrier__TimeoutSeconds=15
Shipping__Carrier__QuoteLifetimeMinutes=15
```

`POST /api/shipping/quote` nhận `destination` và tùy chọn `items`; bỏ `items` để dùng giỏ của tài khoản. API tính kg từ `Product.ShippingWeightGrams × quantity`, không tin `weight` hay phí do khách gửi. Báo giá gắn chủ tài khoản, địa chỉ, fingerprint giỏ/trọng lượng, thời hạn và đơn sử dụng. Checkout truyền `shippingQuoteId`, không truyền tổng phí. Đổi SKU/số lượng/trọng lượng/địa chỉ phải lấy báo giá mới. Một báo giá không tạo được hai đơn khác nhau; checkout replay hợp lệ vẫn trả đúng đơn cũ.

**Giá trị 500 gram là mặc định để nâng cấp SKU cũ an toàn, không phải số cân thực tế.** Admin phải cập nhật trọng lượng đóng gói trước khi bật báo giá thật. Không có báo giá thì luồng thường vẫn dùng phí cố định 30.000 VND, được hiển thị rõ trên storefront. Đơn custom dùng giá trọn gói trong báo giá được khách duyệt; không cộng thêm phí ngoài báo giá. Adapter hiện chỉ lấy giá vận chuyển; tạo vận đơn, mua nhãn, cập nhật tracking tự động theo từng hãng chưa được triển khai.

## Hết hạn và voucher

Worker trong API chạy mỗi phút, xử lý tối đa 500 đơn `Confirmed`/`Unpaid` đã quá 30 phút; transaction Serializable nhả giữ kho và chuyển `Expired` một lần. Không tự hủy đơn đã trả cọc/đã trả đủ hoặc đã được đưa sang xử lý. Custom/preorder không giữ tồn kho thành phẩm; kết thúc yêu cầu không cộng kho giả. Worker chỉ chạy khi ứng dụng đang hoạt động, sẽ bắt kịp các đơn quá hạn khi chạy lại.

Chính sách lượt voucher: hoàn lại **một lần** khi hủy/hết hạn đơn chưa giao, hoặc khi tạo yêu cầu hoàn đơn đủ điều kiện và chuyển đơn sang hủy. Giữ `Order.VoucherId` làm lịch sử. Refund một phần tiếp theo, hoàn tất refund hay gọi hủy lặp không hoàn thêm lượt. Nếu yêu cầu refund bị từ chối sau khi đơn đã hủy thì lượt đã trả vẫn được giữ nguyên; việc chuyển tiền và việc giải phóng cam kết bán hàng là hai mốc khác nhau.

## Email, SMS và tài khoản

```text
Auth__PasswordResetUrlBase=https://shop.example.test/shop/
Notifications__EmailEnabled=true
Notifications__Smtp__Host=<smtp-host>
Notifications__Smtp__Port=587
Notifications__Smtp__EnableSsl=true
Notifications__Smtp__Username=<username>
Notifications__Smtp__Password=<secret>
Notifications__Smtp__From=<sender-email>
Notifications__SmsEnabled=false
Notifications__Sms__Endpoint=https://sms-bridge.example.test/send
Notifications__Sms__ApiKey=<secret>
Notifications__Sms__Sender=<sender>
DataProtection__KeyPath=/srv/hoasap/shared-keys
```

SMS dùng HTTPS JSON bridge; không mặc nhiên tương thích API của mọi nhà mạng. Tắt một kênh không đánh dấu thông báo của kênh đó là đã gửi. Outbox lưu cùng giao dịch tạo/đổi đơn và booking, claim bằng lease, retry có backoff, giới hạn số lần rồi chuyển lỗi cuối. Có nhắc workshop trước buổi học. SMTP/SMS là **at-least-once**, không thể hứa exactly-once nếu provider nhận thành công nhưng ứng dụng mất kết nối trước khi ghi kết quả; dịch vụ nhận SMS nên xử lý `Idempotency-Key` theo message id.

Quên mật khẩu trả thông điệp chung, không trả token trong API. Token có hạn 30 phút, lưu hash; đường dẫn gửi email được bảo vệ bằng ASP.NET Data Protection. Token reset chỉ dùng một lần, đổi/reset mật khẩu thu hồi refresh token và làm access token cũ mất hiệu lực theo security stamp. Refresh token được lưu hash, xoay vòng theo family; reuse phát hiện sẽ thu hồi cả family. Logout thu hồi family của phiên được cung cấp. Storefront giữ token trong bộ nhớ, không ghi localStorage; tải lại trang cần đăng nhập lại.

Các instance phải dùng cùng JWT key, issuer/audience và key ring Data Protection. Bảo vệ thư mục khóa bằng quyền hệ điều hành, mã hóa volume/key ring theo hạ tầng, backup khóa riêng và không đưa vào repo. SMTP credentials và API keys đi qua secret manager của môi trường.

## Media và backup/restore

`Media__StoragePath=/srv/hoasap/media` phải là volume bền vững, và là shared filesystem khi chạy nhiều instance. Đây chưa phải adapter S3/CDN. Upload chỉ PNG/JPEG/WebP đã kiểm tra chữ ký, GUID tên file, tối đa mặc định 900 KiB; đường dẫn vật lý không được expose tùy ý. URL công khai ổn định `/api/media/{fileName}`; không nhận SVG và chặn traversal. Gỡ ảnh khỏi sản phẩm không tự xóa tệp đang có thể dùng chung.

Scripts trong `scripts/` không chứa mật khẩu. SQLite backup dùng Python 3 standard-library backup API (bao gồm WAL), kiểm tra integrity, không đè backup có sẵn. Restore cần dừng mọi API/worker, `CONFIRM_APP_STOPPED=yes` và nhập `RESTORE`; chỉ khôi phục vào đường dẫn mới sau khi đã lưu riêng bộ database/WAL/SHM cũ. Media restore cần thư mục trống, từ chối traversal, link và special file. Kiểm tra lại owner/quyền đọc sau khi phục hồi. SQL Server script cần `sqlcmd` trên máy vận hành, quyền backup phù hợp và đường dẫn backup ở phía SQL Server; dùng COPY_ONLY/CHECKSUM và phục hồi có kiểm soát. SQL scripts mặc định xác minh certificate; chỉ đặt `SQLSERVER_TRUST_CERTIFICATE=yes` cho môi trường test có certificate tự ký. Backup không đè tệp đã tồn tại.

Dừng ghi hoặc dùng snapshot nhất quán để DB, media và key ring cùng mốc. Lưu bản sao mã hóa ngoài máy chủ, đặt retention theo nhu cầu (ví dụ 7 ngày + 4 tuần) và diễn tập restore. Script chỉ là công cụ thủ công, không cài lịch backup tự động. QA đã diễn tập SQLite và media trên dữ liệu mẫu, không thay thế diễn tập SQL Server/volume thật ở staging.

## Nhiều instance và giới hạn lưu lượng

Production mặc định bật limiter phân tán; có thể đặt `RateLimiting__Distributed=true` rõ ràng. Các instance dùng chung database. Cấu hình `PermitLimit=300`, `AuthPermitLimit=15`, `WindowSeconds=60`, `AuthWindowSeconds=60`, `MaxBuckets=100000`; hai cửa sổ phải bằng nhau. Counter tăng nguyên tử trong transaction, có quota auth chặt hơn, saturation và cleanup có giới hạn. DB/cấu hình lỗi thì fail closed bằng 429, không tự chuyển sang memory để vượt quota. Limiter memory vẫn là lớp bảo vệ bổ sung từng process.

Chỉ bật `RateLimiting__TrustForwardedHeaders=true` với `RateLimiting__TrustedProxies__0` (và các chỉ số tiếp theo) là đúng IP của reverse proxy do mình kiểm soát; không tin header do Internet tự gửi. Khi bật mà danh sách rỗng hoặc chứa giá trị không phải IP, ứng dụng từ chối khởi động; không hỗ trợ CIDR trong danh sách này. Forwarded headers được xử lý một lần trước HTTPS redirection và cả hai limiter, nên mọi lớp dùng cùng IP đã chuẩn hóa. Chỉ nhận một hop gần ứng dụng nhất (phần tử bên phải), hỗ trợ cả IPv4-mapped IPv6; proxy phải ghi đè/chuẩn hóa `X-Forwarded-For` và `X-Forwarded-Proto` đúng IP khách và scheme gốc. Với nhiều tầng proxy, proxy sát ứng dụng phải cung cấp IP khách đã xác minh; không tăng mức tin cậy chỉ để đọc phần tử bên trái do khách tự gửi. Khi tắt hoặc peer không thuộc danh sách tin cậy, cả hai header bị bỏ qua, kể cả với loopback. Kestrel/ingress vẫn cần giới hạn kết nối, TLS và chống DDoS ở biên. Limiter SQL không được quảng bá như giải pháp chống DDoS quy mô lớn.

Xem [kịch bản và kết quả smoke-load](../deployment/README.md) và [nâng cấp database](database-upgrade.md). Kết quả local không phải cam kết năng lực production.
