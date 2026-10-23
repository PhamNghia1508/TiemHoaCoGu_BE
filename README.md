# Tiệm Hoa Có Gu — API C#

Backend e-commerce hoa sáp và đặt chỗ workshop, phát triển từ khung dự án và thiết kế trong `TaiNguyen/Note ERD.txt`. ASP.NET Core **.NET 8**, EF Core 8, JWT, Swagger; giữ bốn tầng API → BLL → DAL → Domain.

## Chạy nhanh

Cài .NET SDK 8 hoặc mới hơn có runtime .NET 8. Không cần SQL Server để thử: bản phát triển dùng SQLite.

```bash
ASPNETCORE_ENVIRONMENT=Development dotnet run --project HoaSap.API --no-launch-profile --urls http://localhost:5080
```

Mở cửa hàng tại **`http://localhost:5080/shop/`**, CMS tại **`http://localhost:5080/admin`**; tài liệu API tại `http://localhost:5080/swagger`. Liveness: `GET /health`; readiness kiểm tra truy vấn database/schema: `GET /health/ready` (503 nếu chưa sẵn sàng).

Development tự tạo database `hoasap.db`, seed danh mục, sản phẩm có tồn kho và một workshop có ca tương lai. Không có tài khoản quản trị/mật khẩu mặc định. Nếu chưa cấu hình khóa JWT, Development tự tạo khóa ngẫu nhiên; token cũ hết hiệu lực sau khi khởi động lại.

Tạo quản trị viên lần đầu bằng biến môi trường, rồi gỡ các biến bootstrap sau khi tạo. Không dùng mật khẩu thật trong file cấu hình hoặc commit:

```bash
export BootstrapAdmin__Email='admin@example.test'
export BootstrapAdmin__PhoneNumber='0900000001'
export BootstrapAdmin__Password='<mat-khau-rieng-tu-12-ky-tu>'
export Jwt__Key='<khoa-ngau-nhien-it-nhat-32-byte>'
ASPNETCORE_ENVIRONMENT=Development dotnet run --project HoaSap.API --no-launch-profile --urls http://localhost:5080
```

Đăng ký thường luôn tạo role `Customer`, không nhận role từ client. Chỉ **Admin** được chỉnh catalog, tồn kho, voucher và tài khoản qua CMS. Admin/Staff giữ các luồng nghiệp vụ đơn hàng/workshop và ghi nhận thanh toán hiện có; không xây dựng bộ quyền Staff tùy biến đầy đủ. Các endpoint cá nhân chỉ truy cập dữ liệu của tài khoản đang đăng nhập. Mật khẩu được băm PBKDF2-SHA256 có salt, không xuất hiện trong response.

## Các nhóm API

Tài liệu schema/request chi tiết nằm trong Swagger sinh trực tiếp từ mã nguồn.

| Nhóm | Endpoint tiêu biểu | Quyền |
|---|---|---|
| Tài khoản | `POST /api/auth/register`, `POST /api/auth/login`, `GET /api/auth/me` | Public / tài khoản |
| Đổi mật khẩu | `POST /api/auth/change-password` | Tài khoản đang đăng nhập |
| Nhật ký nghiệp vụ | `GET /api/management/audit-logs` | Chỉ Admin |
| Catalog | `GET /api/catalog/categories`, `GET /api/catalog/products`, `GET /api/catalog/products/{id}` | Public |
| Sổ địa chỉ | `GET/POST /api/addresses`, `PUT/DELETE /api/addresses/{id}` | Chủ tài khoản |
| Giỏ hàng | `/api/cart` | Chủ tài khoản |
| Đặt hàng | `POST /api/orders/checkout` | Khách vãng lai / tài khoản |
| Đơn hàng | `GET /api/orders`, `GET /api/orders/{id}`, `POST /api/orders/{id}/cancel` | Chủ đơn; danh sách tối đa 200 đơn mới nhất |
| Giao hàng | `GET /api/orders/{id}/deliveries` | Chủ đơn |
| CMS | `/api/management/*`, `/admin` | API chỉ Admin; trang đăng nhập public |
| Nghiệp vụ đơn | `GET /api/orders/staff`, trạng thái và deliveries | Admin/Staff |
| Trạng thái giao | `POST /api/orders/{id}/deliveries/{deliveryId}/status` | Admin/Staff |
| Workshop | `/api/workshops`, `/{id}/sessions` | Đọc public; ghi Admin/Staff |
| Đặt vé | `POST /api/bookings/sessions/{sessionId}`, `GET /api/bookings` | Khách vãng lai / chủ vé |
| Check-in | `/api/bookings/{id}/check-in`, `/no-show` | Admin/Staff |
| Ghi nhận tiền | `POST /api/payments/order/{id}`, `/booking/{id}` | Admin/Staff |
| Ghi nhận hoàn tiền | `POST /api/bookings/{id}/refund` | Admin/Staff |
| Danh sách ca học | `GET /api/workshops/sessions/{id}/roster`, `POST /api/workshops/sessions/{id}/close` | Admin/Staff |
| Yêu cầu hoàn đơn | `POST /api/refunds/order/{id}`, `GET /api/refunds?page=1&pageSize=20` | Admin/Staff |
| Duyệt/từ chối hoàn | `POST /api/refunds/{id}/approve`, `/reject` | Chỉ Admin |
| Ghi nhận đã hoàn | `POST /api/refunds/{id}/process` | Chỉ Admin; cần `transactionCode` |

| Hoa đặt riêng/preorder | `/api/custom-orders`, `/{id}/quotations`, `/quotations/{id}/approve`, `/checkout` | Chủ yêu cầu; báo giá chỉ Admin; vận hành giới hạn Admin/Staff |
| Khôi phục/phiên đăng nhập | `/api/auth/forgot-password`, `/reset-password`, `/refresh`, `/logout` | Token một lần / phiên của chính tài khoản |
| Báo giá giao hàng | `POST /api/shipping/quote` | Tài khoản, phí/khối lượng tính ở server |
| Thu tiền qua adapter | `/api/gateway/orders/{id}/balance`, `/charge`, `/webhook` | Chủ đơn; webhook có chữ ký |
| Đối soát thu tiền | `/api/gateway/reconciliation` | Chỉ Admin |
| Hoàn qua adapter | `/api/refunds/{id}/process-gateway`, `/reconcile-gateway` | Chỉ Admin |

Catalog hỗ trợ `page`, `pageSize` (tối đa 100), `search`, `categoryId`, `section`. Header đăng nhập: `Authorization: Bearer <accessToken>`.

## CMS Admin web

Mở `/admin` và đăng nhập bằng tài khoản có role **Admin**. Giao diện HTML/CSS/JavaScript được phục vụ cùng API, không cần build Node hoặc máy chủ frontend riêng. JWT chỉ giữ trong bộ nhớ trang; tải lại trang cần đăng nhập lại. UI không cấp quyền bảo mật: mọi endpoint CMS vẫn kiểm tra Admin ở server. Khóa tài khoản, đổi role hoặc reset mật khẩu làm token cũ bị từ chối ngay trên request tiếp theo. Không cho tự khóa/tự hạ quyền Admin; khách vãng lai không được chuyển thành tài khoản đăng nhập.

| Chức năng | Endpoint (tiền tố `/api/management`) |
|---|---|
| Sản phẩm, kể cả đã ngừng bán | `GET/POST /products`, `GET/PUT/DELETE /products/{id}`, `PATCH /products/{id}/active?value=false` |
| Danh mục và ảnh | `GET/POST /categories`, `PUT/DELETE /categories/{id}`; trường `imageUrl` |
| Thư viện ảnh sản phẩm | `GET/POST /products/{id}/images`, `PUT/DELETE /products/{id}/images/{imageId}`; `imageUrl`, `displayOrder` |
| Tồn kho khả dụng | `PUT /inventory/{productId}?quantity=...`; không đổi lượng đang giữ |
| Tài khoản | `GET/POST /accounts`, `PUT /accounts/{id}`, `POST /accounts/{id}/reset-password` |
| Ma trận quyền | `GET /permissions`; Admin quản trị CMS, Staff chỉ giữ các nghiệp vụ sẵn có, Customer chỉ dữ liệu của mình |
| Voucher | `GET/POST /vouchers`, `PUT /vouchers/{id}`, `PATCH /vouchers/{id}/active?value=false` |
| Đơn hàng | `GET /orders`, `GET /orders/{id}`; lọc `search`, `status`, `paymentStatus`, `from`, `to`, `page`, `pageSize` |
| Dashboard, báo cáo | `GET /dashboard?from=...&to=...`, `GET /reports/orders.csv` |
| Upload ảnh | `POST /media`, multipart trường `file`; trả `{url,fileName,contentType,size}` |

Danh sách sản phẩm/tài khoản/voucher/đơn trả `{items,total,page,pageSize}` với `pageSize` tối đa 100. Danh mục trả mảng. Không xóa danh mục còn sản phẩm hoặc sản phẩm đang được tham chiếu trong giỏ/đơn; hãy tắt sản phẩm để giữ lịch sử. Voucher sửa không được giảm giới hạn dưới số lượt đã dùng; dừng voucher không xóa lịch sử sử dụng. Báo cáo CSV có xử lý ô có nguy cơ công thức bảng tính; mốc thời gian ISO-8601 theo UTC, `from` bao gồm và `to` không bao gồm. Dashboard mặc định 30 ngày UTC, khoảng chọn tối đa 366 ngày. Tiền thu/hoàn được tổng hợp cho nhóm đơn **tạo trong khoảng đã chọn**, không phải báo cáo dòng tiền theo ngày giao dịch; số khách/sản phẩm/tồn thấp là số toàn hệ thống. Doanh thu đơn không đồng nghĩa tiền thực thu; các chỉ số dựa trên ghi sổ, không phải xác nhận ngân hàng. CSV tối đa 10.000 đơn; vượt giới hạn trả lỗi yêu cầu thu hẹp bộ lọc, không âm thầm cắt dữ liệu.

**Ảnh:** upload PNG/JPEG/WebP tối đa **900 KiB**, kiểm tra chữ ký định dạng, không nhận SVG/HTML hoặc dùng tên file do client cung cấp. API công khai `GET /api/media/{fileName}` chỉ phục vụ tên sinh ngẫu nhiên hợp lệ với MIME cố định và `nosniff`. Cũng có thể dùng URL HTTP(S) ngoài hoặc đường dẫn ảnh nội bộ do upload trả về. Đây không phải dịch vụ quét mã độc hay tái mã hóa ảnh. Tệp mặc định nằm ngoài webroot ở `HoaSap.API/App_Data/media`; production cần cấu hình `Media__StoragePath` trên volume bền vững, backup riêng và dùng shared storage nếu chạy nhiều instance. Xóa ảnh khỏi sản phẩm/danh mục chỉ gỡ liên kết; tệp không tự xóa để tránh làm hỏng liên kết dùng chung. Chưa có tự dọn tệp không dùng.

## Quy tắc nghiệp vụ

**Đơn hàng:** Giá lấy và chụp từ database, không tin tổng tiền/phí do khách gửi. Có thể dùng báo giá vận chuyển được lưu theo chủ tài khoản, giỏ, địa chỉ và thời hạn; không có báo giá thì phí mặc định 30.000 VND. Trọng lượng lấy từ SKU do Admin cấu hình. Checkout giữ kho trong transaction; thiếu hàng rollback. Hủy/hết hạn đơn trả kho và lượt voucher đúng một lần, giữ liên kết voucher để tra lịch sử. Worker mỗi phút hết hạn đơn `Confirmed`/`Unpaid` quá 30 phút; không tự hủy đơn đã cọc/đã xử lý. Preorder/custom đi qua yêu cầu → báo giá có version → khách duyệt → checkout → cọc → sản xuất → sẵn sàng giao → hoàn tất; không giữ hoặc cộng kho thành phẩm giả. Giá trọn gói, tiền cọc và ngày hoàn thành lấy từ revision khách đã duyệt; đổi revision làm mất duyệt cũ.

Phiếu giao đi theo `Preparing → Shipped → Delivered`; chỉ `Preparing` mới được hủy. Trạng thái đơn đi theo `Confirmed → Processing → Delivering → Completed`. Chỉ hoàn tất khi đã thu đủ tiền **và** tổng số lượng `Delivered` đủ từng dòng; lúc đó trừ kho đã giữ, có kiểm tra số dòng cập nhật. `shippedQuantity` trên DTO cũ biểu thị số lượng đã phân bổ vào phiếu chưa hủy, không phải bằng chứng giao thành công; xem `deliveryStatus` để xác định thực giao.

**Chống tạo trùng:** Checkout và giữ chỗ nhận `idempotencyKey` tùy chọn, tối đa 100 ký tự. Client nên luôn gửi UUID ngẫu nhiên mới cho mỗi thao tác đặt mới, giữ nguyên key và payload khi thử lại do timeout. Cùng key/cùng tài khoản/cùng payload trả lại đơn/vé cũ; khác payload trả 409. Key của khách vãng lai phải được giữ bí mật, không dùng lại giữa khách. Index database bảo vệ cả trường hợp request chạy đồng thời; khi gặp 409 do cạnh tranh, thử lại với **cùng key**, không tạo key mới. Booking đã hết hạn không được hồi sinh khi replay. Không dùng lại key của đơn/vé đã hủy cho một đơn/vé mới.

**Workshop:** Giữ chỗ 10 phút, cọc 30%; số vé và tiền do server tính. Worker kiểm tra hết hạn mỗi 30 giây trong tiến trình API. Khi API dừng worker cũng dừng; khi chạy lại sẽ xử lý các hold đã hết hạn. Đây không phải hàng đợi phân tán. Không tra cứu công khai thông tin cá nhân chỉ bằng mã đơn/mã booking.

Đơn đã thu tiền không hủy qua endpoint thường: nhân viên tạo yêu cầu hoàn, hệ thống kiểm tra chưa giao, hủy đơn và trả kho/lượt voucher một lần. Hoàn tiền đi `Requested → Approved → Processed` hoặc `Rejected`; chỉ Admin duyệt/xử lý/đối soát hoàn đơn. Hỗ trợ ghi nhận chuyển tiền ngoài hệ thống hoặc adapter hoàn tiền có attempt/idempotency bền vững. Khoản chưa rõ kết quả không tự gửi lại. Tổng yêu cầu đang giữ + đã hoàn không vượt tiền đã thu.

Với workshop, lần đầu phải đủ cọc; phần còn lại phải được ghi nhận trước buổi học 24 giờ. Đặt trong 24 giờ cuối phải trả đủ ngay lần đầu. Không tự đổi thời gian/địa điểm ca đang có booking hoạt động. Check-in chỉ từ lúc ca bắt đầu và sau khi thu đủ; no-show chỉ sau giờ kết thúc, theo múi giờ Việt Nam. Hủy vé đã cọc tạo yêu cầu hoàn chờ Admin duyệt qua `/api/refunds`; không phải cam kết hoàn tiền theo chính sách kinh doanh đã được duyệt.

**Thanh toán:** Giữ cách ghi nhận thủ công `Cash`/`BankTransfer` sau khi kiểm tra tiền, đồng thời bổ sung payment intent, callback có chữ ký, chống replay, thanh toán cọc/phần còn lại, refund attempt và đối soát Admin. Adapter HTTPS/HMAC hiện là hợp đồng tổng quát, **không phải tích hợp VNPay/MoMo/GHN đã hoạt động**. Chưa có khóa/merchant/SMTP/SMS thật thì không gửi giao dịch/tin nhắn thật. Xem [runbook cấu hình và giới hạn](docs/operations.md).

## Bảo mật tài khoản và truy vết

Đổi mật khẩu gửi `{ "currentPassword": "...", "newPassword": "...", "confirmNewPassword": "..." }`; thành công trả `204`, mật khẩu dài 10–128 ký tự gồm hoa/thường/số/ký tự đặc biệt. Có quên mật khẩu qua token hạn 30 phút gửi bằng outbox, token reset dùng một lần, refresh token được hash và xoay vòng theo family; reuse thu hồi cả family. Đổi/reset mật khẩu hoặc Admin thay thông tin bảo mật thu hồi refresh và link reset cũ; security stamp từ chối access token cũ. Storefront giữ token trong bộ nhớ, không localStorage. Production phải cấu hình URL khôi phục HTTPS đúng `/shop/` và shared Data Protection key ring.

Audit log ghi các trường nghiệp vụ theo allowlist của Customer (role/trạng thái, không chép thông tin liên hệ), Category, ProductImage, Product, Inventory, Voucher, Order, Delivery, WorkshopSession, WorkshopBooking, Payment, Refund, custom quotation/revision, gateway và shipping quote khi lưu qua EF ChangeTracker. Mỗi bản ghi có entity/id, thao tác, giá trị trước/sau, giờ UTC, actor/role và trace ID; được lưu cùng transaction với dữ liệu nghiệp vụ. Không snapshot toàn bộ object; loại mật khẩu, thông tin liên hệ/địa chỉ khách, mô tả/ghi chú, URL ảnh có thể chứa token và nội dung phản hồi cổng thanh toán. Tác vụ nền/khách vãng lai có thể không có actor ID. Không cho sửa/xóa audit qua SaveChanges, nhưng đây **không phải kho nhật ký chống can thiệp ở mức database**; cần giới hạn quyền DB và chính sách lưu trữ riêng.

Admin tra cứu `GET /api/management/audit-logs?entityType=Order&entityId=<guid>&actorId=<guid>&page=1&pageSize=50`; `pageSize` tối đa 100. Bộ lọc `from`/`to` nên truyền ISO-8601 có múi giờ, ví dụ `2026-10-22T00:00:00Z`. Các bulk update giữ kho khi checkout, nhả kho khi hủy/yêu cầu hoàn tiền, trừ kho đã giữ khi hoàn tất đơn và tăng lượt voucher dùng `ExecuteUpdateAuditedAsync`: snapshot trước/sau theo cùng allowlist, actor/role/trace và audit được ghi trong transaction Serializable của nghiệp vụ. Điều kiện đủ kho/lượt voucher vẫn được kiểm tra ở câu UPDATE; thao tác thất bại hoặc rollback không để lại log trong database. Inventory đổi concurrency token sau bulk update để ngăn ghi đè từ bản đọc cũ. **ExecuteUpdate/ExecuteDelete thông thường và SQL trực tiếp vẫn không tự có audit**; đường ghi mới phải chủ động tích hợp audit và kiểm thử rollback.

Response có `X-Request-ID` và `traceId`; log không ghi body/query/token. Mặc định API 300/phút/IP, auth 15/phút/IP. Có lớp memory từng process và limiter database dùng chung nhiều instance, cập nhật nguyên tử/transaction, bounded cleanup, 429 + `Retry-After`, fail-closed nếu cấu hình/database lỗi. Hai giá trị `WindowSeconds`/`AuthWindowSeconds` phải bằng nhau. Forwarded IP chỉ được tin từ proxy đã cấu hình; xem [vận hành nhiều instance](docs/operations.md).

## Cơ sở dữ liệu và triển khai

SQL Server 2022 Development có compose tại `TaiNguyen/docker-compose.db.yml`; yêu cầu tự đặt `MSSQL_SA_PASSWORD`, cổng chỉ bind localhost. Cấu hình API:

```bash
export Database__Provider=SqlServer
export ConnectionStrings__DefaultConnection='Server=localhost,1433;Database=HoaSap;User Id=sa;Password=<password>;TrustServerCertificate=True'
```

SQLite Development dùng `EnsureCreated` cho database mới; database đã có dữ liệu dùng công cụ `HoaSap.Maintenance` offline để dry-run, backup, nâng cấp cộng thêm trong transaction và kiểm tra toàn vẹn/row count. Không xóa database thật để cập nhật schema. Có fixture lấy từ schema commit `13d90e6`, kiểm thử giữ nguyên tài khoản/đơn/tiền/kho/voucher khi nâng cấp. Xem [hướng dẫn nâng cấp](docs/database-upgrade.md).

Đã có **EF migration SQL Server cho database mới** và script idempotent tại `HoaSap.DAL/Migrations/sqlserver-deploy.sql`. Script không phải migration nâng cấp/baseline cho database MVP đã có bảng nhưng chưa có lịch sử migrations. Với database cũ, không chạy thẳng script hoặc tự đánh dấu migration đã áp dụng: cần so sánh schema, sao lưu và kiểm thử phương án chuyển dữ liệu trên bản sao.

```bash
dotnet tool restore
export HOASAP_SQLSERVER_CONNECTION='<connection-string-tu-secret-moi-truong>'
dotnet ef database update --project HoaSap.DAL --startup-project HoaSap.DAL
```

Factory ưu tiên `HOASAP_SQLSERVER_CONNECTION`, sau đó `ConnectionStrings__DefaultConnection`; cũng có thể truyền `--connection` bằng secret của môi trường triển khai. Review script trước khi áp dụng; ứng dụng không tự chạy migration lúc khởi động. Production từ chối `Database__Initialize=true` hoặc `Database__Seed=true`; bắt buộc có `Jwt__Key`, database đã chuẩn bị, HTTPS/reverse proxy cấu hình đúng và CORS allowlist bằng `Cors__AllowedOrigins__0`… Swagger chỉ mở trong Development. Giới hạn body mặc định 1 MiB.

Migration lifecycle bổ sung bảng báo giá/revision, phiên tài khoản/outbox, gateway/refund attempt, shipping quote, rate counter và trọng lượng SKU. Với SQL Server có lịch sử EF hợp lệ, backup và review/apply script idempotent trước khi chạy API. Database SQL Server không có lịch sử EF cần kế hoạch baseline/backfill đã kiểm duyệt, không tự đóng dấu migrations. Readiness kiểm tra các bảng/cột mới và trả 503 nếu thiếu.

```bash
dotnet build HoaSap.sln -c Release
dotnet test HoaSap.Tests/HoaSap.Tests.csproj -c Release
docker build -t hoasap-api .
```

Kiểm chứng của bản mở rộng được ghi trong [QA lifecycle](docs/qa-lifecycle.md), gồm suite tự động, migration có dữ liệu, smoke-load hai process SQL Server, quota phân tán và drill backup/restore. Kết quả local không phải chứng nhận production hoặc xác nhận GitHub Actions đã chạy xanh.

Dockerfile chỉ đóng gói API; không kèm secrets/database. CI GitHub Actions build/test .NET 8 và khai báo SQL Server 2022 service. Kiểm thử SQL Server opt-in qua `HOASAP_TEST_SQLSERVER`: tạo database tên GUID riêng, áp dụng migration và xóa database test khi kết thúc; chỉ dùng tài khoản/máy chủ test có quyền tạo/xóa database. Nếu thiếu biến này, cả 9 test SQL Server được đánh dấu **Skipped**, không tính là đã kiểm chứng SQL Server. Các test HTTP API và helper SQLite vẫn dùng SQLite; test SQL Server mới gọi service/DbContext với database thật. Kết quả QA nêu trên chạy cục bộ, không phải xác nhận một lần chạy GitHub Actions đã thành công.

CMS giữ các default nâng cấp an toàn `Customers.IsActive=true`, `Categories.ImageUrl=""`; không khóa tài khoản cũ. Có UI quản trị workshop/ca/roster/check-in và báo giá/tiến độ custom trong `/admin`, storefront trong `/shop`. Trọng lượng SKU cũ mặc định 500 gram và cần được cân/cập nhật trước khi dùng giá carrier thật.

## Cấu trúc và các điểm cần biết

- `HoaSap.Domain`: entity thương mại, báo giá/revision, identity/outbox, gateway/carrier và rate counter.
- `HoaSap.DAL`: EF Core, quan hệ/index/constraints, seed catalog.
- `HoaSap.BLL`: nghiệp vụ commerce và workshop.
- `HoaSap.API`: controllers, JWT, rate limit, lỗi ProblemDetails, worker hết hạn.
- `HoaSap.Tests`: kiểm thử tự động.

Hai điều chỉnh so với sơ đồ ban đầu: `Category.Id` dùng GUID thay vì số nguyên; đơn/vé khách vãng lai có thể để `CustomerId` null và lưu snapshot thông tin đặt chỗ/giao nhận, không tự liên kết vào tài khoản đăng ký chỉ vì trùng số điện thoại. Giúp tránh gán nhầm dữ liệu riêng của khách.

Các nhóm việc còn lại đã có triển khai trong repo, nhưng **chưa bật bán hàng thật**. Cần chọn/triển khai provider tương thích, cấp merchant/secrets, cấu hình SMTP/SMS, domain HTTPS, shared media/key ring và kiểm chứng sandbox trên hạ tầng đích. Chưa có tạo vận đơn/tracking tự động theo hãng, S3/CDN, lịch backup hạ tầng tự cài hoặc chính sách kinh doanh tự duyệt hoàn tiền. Worker/outbox có retry/lease nhưng không hứa exactly-once delivery tới provider. Khóa Staff vẫn giới hạn, không có CMS hay quyền tài chính gateway mới. Xem [vận hành](docs/operations.md), [migration](docs/database-upgrade.md) và [load test](deployment/README.md).
