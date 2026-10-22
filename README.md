# Tiệm Hoa Có Gu — API C#

Backend e-commerce hoa sáp và đặt chỗ workshop, phát triển từ khung dự án và thiết kế trong `TaiNguyen/Note ERD.txt`. ASP.NET Core **.NET 8**, EF Core 8, JWT, Swagger; giữ bốn tầng API → BLL → DAL → Domain.

## Chạy nhanh

Cài .NET SDK 8 hoặc mới hơn có runtime .NET 8. Không cần SQL Server để thử: bản phát triển dùng SQLite.

```bash
ASPNETCORE_ENVIRONMENT=Development dotnet run --project HoaSap.API --no-launch-profile --urls http://localhost:5080
```

Mở `http://localhost:5080/swagger`. Liveness: `GET /health`; readiness kiểm tra truy vấn database/schema: `GET /health/ready` (503 nếu chưa sẵn sàng).

Development tự tạo database `hoasap.db`, seed danh mục, sản phẩm có tồn kho và một workshop có ca tương lai. Không có tài khoản quản trị/mật khẩu mặc định. Nếu chưa cấu hình khóa JWT, Development tự tạo khóa ngẫu nhiên; token cũ hết hiệu lực sau khi khởi động lại.

Tạo quản trị viên lần đầu bằng biến môi trường, rồi gỡ các biến bootstrap sau khi tạo. Không dùng mật khẩu thật trong file cấu hình hoặc commit:

```bash
export BootstrapAdmin__Email='admin@example.test'
export BootstrapAdmin__PhoneNumber='0900000001'
export BootstrapAdmin__Password='<mat-khau-rieng-tu-12-ky-tu>'
export Jwt__Key='<khoa-ngau-nhien-it-nhat-32-byte>'
ASPNETCORE_ENVIRONMENT=Development dotnet run --project HoaSap.API --no-launch-profile --urls http://localhost:5080
```

Đăng ký thường luôn tạo role `Customer`, không nhận role từ client. Admin/Staff mới được chỉnh catalog, tồn kho, voucher, đơn hàng và ghi nhận thanh toán. Các endpoint cá nhân chỉ truy cập dữ liệu của tài khoản đang đăng nhập. Mật khẩu được băm PBKDF2-SHA256 có salt, không xuất hiện trong response.

## Các nhóm API

Tài liệu schema/request chi tiết nằm trong Swagger sinh trực tiếp từ mã nguồn.

| Nhóm | Endpoint tiêu biểu | Quyền |
|---|---|---|
| Tài khoản | `POST /api/auth/register`, `POST /api/auth/login`, `GET /api/auth/me` | Public / tài khoản |
| Catalog | `GET /api/catalog/categories`, `GET /api/catalog/products`, `GET /api/catalog/products/{id}` | Public |
| Sổ địa chỉ | `GET/POST /api/addresses`, `PUT/DELETE /api/addresses/{id}` | Chủ tài khoản |
| Giỏ hàng | `/api/cart` | Chủ tài khoản |
| Đặt hàng | `POST /api/orders/checkout` | Khách vãng lai / tài khoản |
| Đơn hàng | `GET /api/orders`, `GET /api/orders/{id}`, `POST /api/orders/{id}/cancel` | Chủ đơn; danh sách tối đa 200 đơn mới nhất |
| Giao hàng | `GET /api/orders/{id}/deliveries` | Chủ đơn |
| Quản trị | `/api/management/*`, `GET /api/orders/staff`, trạng thái và deliveries | Admin/Staff |
| Trạng thái giao | `POST /api/orders/{id}/deliveries/{deliveryId}/status` | Admin/Staff |
| Workshop | `/api/workshops`, `/{id}/sessions` | Đọc public; ghi Admin/Staff |
| Đặt vé | `POST /api/bookings/sessions/{sessionId}`, `GET /api/bookings` | Khách vãng lai / chủ vé |
| Check-in | `/api/bookings/{id}/check-in`, `/no-show` | Admin/Staff |
| Ghi nhận tiền | `POST /api/payments/order/{id}`, `/booking/{id}` | Admin/Staff |
| Ghi nhận hoàn tiền | `POST /api/bookings/{id}/refund` | Admin/Staff |
| Danh sách ca học | `GET /api/workshops/sessions/{id}/roster`, `POST /api/workshops/sessions/{id}/close` | Admin/Staff |
| Yêu cầu hoàn đơn | `POST /api/refunds/order/{id}`, `GET /api/refunds?page=1&pageSize=20` | Admin/Staff |
| Duyệt/từ chối hoàn | `POST /api/refunds/{id}/approve`, `/reject` | Chỉ Admin |
| Ghi nhận đã hoàn | `POST /api/refunds/{id}/process` | Admin/Staff; cần `transactionCode` |

Catalog hỗ trợ `page`, `pageSize` (tối đa 100), `search`, `categoryId`, `section`. Header đăng nhập: `Authorization: Bearer <accessToken>`.

## Quy tắc nghiệp vụ

**Đơn hàng:** Giá lấy từ database và chụp vào từng dòng đơn, không nhận giá/tổng tiền từ client. Phí giao hàng là **30.000 VND/đơn**, không phải báo giá của đơn vị vận chuyển. Voucher kiểm tra thời gian, giới hạn lượt, giá trị tối thiểu và mức trần. Checkout giữ tồn kho trong transaction; thiếu hàng phải rollback. Hủy đúng trạng thái nhả kho một lần. Không hủy khi còn delivery khác `Cancelled`. Có tách nhiều delivery với kiểm tra tổng số lượng không vượt từng dòng đơn; phiếu đã hủy không chiếm lượng được phân bổ. Sản phẩm preorder chưa được hỗ trợ.

Phiếu giao đi theo `Preparing → Shipped → Delivered`; chỉ `Preparing` mới được hủy. Trạng thái đơn đi theo `Confirmed → Processing → Delivering → Completed`. Chỉ hoàn tất khi đã thu đủ tiền **và** tổng số lượng `Delivered` đủ từng dòng; lúc đó trừ kho đã giữ, có kiểm tra số dòng cập nhật. `shippedQuantity` trên DTO cũ biểu thị số lượng đã phân bổ vào phiếu chưa hủy, không phải bằng chứng giao thành công; xem `deliveryStatus` để xác định thực giao.

**Chống tạo trùng:** Checkout và giữ chỗ nhận `idempotencyKey` tùy chọn, tối đa 100 ký tự. Client nên luôn gửi UUID ngẫu nhiên mới cho mỗi thao tác đặt mới, giữ nguyên key và payload khi thử lại do timeout. Cùng key/cùng tài khoản/cùng payload trả lại đơn/vé cũ; khác payload trả 409. Key của khách vãng lai phải được giữ bí mật, không dùng lại giữa khách. Index database bảo vệ cả trường hợp request chạy đồng thời; khi gặp 409 do cạnh tranh, thử lại với **cùng key**, không tạo key mới. Booking đã hết hạn không được hồi sinh khi replay. Không dùng lại key của đơn/vé đã hủy cho một đơn/vé mới.

**Workshop:** Giữ chỗ 10 phút, cọc 30%; số vé và tiền do server tính. Worker kiểm tra hết hạn mỗi 30 giây trong tiến trình API. Khi API dừng worker cũng dừng; khi chạy lại sẽ xử lý các hold đã hết hạn. Đây không phải hàng đợi phân tán. Không tra cứu công khai thông tin cá nhân chỉ bằng mã đơn/mã booking.

Đơn đã ghi nhận tiền không được hủy qua endpoint hủy thường: nhân viên tạo yêu cầu tại `/api/refunds/order/{id}`; hệ thống kiểm tra chưa giao, hủy đơn và trả kho an toàn. Hoàn tiền theo `Requested → Approved → Processed`, hoặc `Requested → Rejected`; Admin duyệt trước khi nhân viên ghi nhận đã hoàn, mã giao dịch hoàn chống ghi nhận lặp. Tổng yêu cầu đang giữ + đã hoàn không vượt tiền đã thu; `PaymentStatus` thể hiện `PartiallyRefunded`/`Refunded`. Quy trình này chỉ ghi sổ sau khi nhân viên đã kiểm tra/chuyển tiền ngoài hệ thống.

Với workshop, lần đầu phải đủ cọc; phần còn lại phải được ghi nhận trước buổi học 24 giờ. Đặt trong 24 giờ cuối phải trả đủ ngay lần đầu. Không tự đổi thời gian/địa điểm ca đang có booking hoạt động. Check-in chỉ từ lúc ca bắt đầu và sau khi thu đủ; no-show chỉ sau giờ kết thúc, theo múi giờ Việt Nam. Hủy vé đã cọc tạo yêu cầu hoàn chờ Admin duyệt qua `/api/refunds`; không phải cam kết hoàn tiền theo chính sách kinh doanh đã được duyệt.

**Thanh toán:** Chỉ hỗ trợ **ghi nhận thủ công** `Cash` hoặc `BankTransfer` bởi nhân viên đã kiểm tra tiền thực tế. Endpoint không thực hiện chuyển tiền, không kết nối VNPay/MoMo/BankingQR, không tự xác minh giao dịch ngân hàng. `transactionCode` là khóa chống ghi nhận lặp. Ghi nhận refund cũng chỉ là sổ sách, không tự hoàn tiền ra ngân hàng.

## Cơ sở dữ liệu và triển khai

SQL Server 2022 Development có compose tại `TaiNguyen/docker-compose.db.yml`; yêu cầu tự đặt `MSSQL_SA_PASSWORD`, cổng chỉ bind localhost. Cấu hình API:

```bash
export Database__Provider=SqlServer
export ConnectionStrings__DefaultConnection='Server=localhost,1433;Database=HoaSap;User Id=sa;Password=<password>;TrustServerCertificate=True'
```

SQLite Development dùng `EnsureCreated` cho database mới. Khi đã có database MVP cũ, phải sao lưu và lập phương án nâng cấp trước; `EnsureCreated` không thêm cột vào database cũ. Với database SQLite thử nghiệm có thể bỏ file cũ sau khi đã xác nhận không cần giữ dữ liệu; không áp dụng cách này cho dữ liệu thật.

Đã có **EF migration SQL Server cho database mới** và script idempotent tại `HoaSap.DAL/Migrations/sqlserver-deploy.sql`. Script không phải migration nâng cấp/baseline cho database MVP đã có bảng nhưng chưa có lịch sử migrations. Với database cũ, không chạy thẳng script hoặc tự đánh dấu migration đã áp dụng: cần so sánh schema, sao lưu và kiểm thử phương án chuyển dữ liệu trên bản sao.

```bash
dotnet tool restore
export HOASAP_SQLSERVER_CONNECTION='<connection-string-tu-secret-moi-truong>'
dotnet ef database update --project HoaSap.DAL --startup-project HoaSap.DAL
```

Factory ưu tiên `HOASAP_SQLSERVER_CONNECTION`, sau đó `ConnectionStrings__DefaultConnection`; cũng có thể truyền `--connection` bằng secret của môi trường triển khai. Review script trước khi áp dụng; ứng dụng không tự chạy migration lúc khởi động. Production từ chối `Database__Initialize=true` hoặc `Database__Seed=true`; bắt buộc có `Jwt__Key`, database đã chuẩn bị, HTTPS/reverse proxy cấu hình đúng và CORS allowlist bằng `Cors__AllowedOrigins__0`… Swagger chỉ mở trong Development. Giới hạn body mặc định 1 MiB.

```bash
dotnet build HoaSap.sln -c Release
dotnet test HoaSap.Tests/HoaSap.Tests.csproj -c Release
docker build -t hoasap-api .
```

Lần QA của bản bổ sung đơn hàng/workshop: **51 test đạt, 0 lỗi, 1 test SQL Server thực bị bỏ qua**; Release build 0 cảnh báo/0 lỗi. Có test giữ món hàng/chỗ cuối cùng đồng thời, checkout/booking replay, phân quyền chủ dữ liệu, hoàn tất giao hàng, hủy/nhả kho, cọc/check-in và duyệt/ghi nhận hoàn tiền. Đây là kiểm thử chức năng, không phải kiểm thử tải hoặc xác nhận SQL Server thực.

Dockerfile chỉ đóng gói API; không kèm secrets/database. CI GitHub Actions build/test .NET 8 và khai báo SQL Server 2022 service. Kiểm thử SQL Server opt-in qua `HOASAP_TEST_SQLSERVER`: tạo database tên GUID riêng, áp dụng migration và xóa database test khi kết thúc; chỉ dùng tài khoản/máy chủ test có quyền tạo/xóa database. Nếu thiếu biến này, test SQL Server được đánh dấu **Skipped**, không tính là đã kiểm chứng SQL Server. Các test API còn lại dùng SQLite. Cấu hình CI mới chưa được xác nhận chạy thành công trên GitHub khi chưa push.

## Cấu trúc và các điểm cần biết

- `HoaSap.Domain`: 20 entity, các khóa GUID.
- `HoaSap.DAL`: EF Core, quan hệ/index/constraints, seed catalog.
- `HoaSap.BLL`: nghiệp vụ commerce và workshop.
- `HoaSap.API`: controllers, JWT, rate limit, lỗi ProblemDetails, worker hết hạn.
- `HoaSap.Tests`: kiểm thử tự động.

Hai điều chỉnh so với sơ đồ ban đầu: `Category.Id` dùng GUID thay vì số nguyên; đơn/vé khách vãng lai có thể để `CustomerId` null và lưu snapshot thông tin đặt chỗ/giao nhận, không tự liên kết vào tài khoản đăng ký chỉ vì trùng số điện thoại. Giúp tránh gán nhầm dữ liệu riêng của khách.

Backend đã được củng cố cho các luồng đơn hàng/workshop, nhưng **chưa được chứng nhận sẵn sàng vận hành bán hàng thật**. Chưa có frontend, upload ảnh/media storage, email/SMS, quên mật khẩu/refresh token, điều chỉnh giá custom có lịch sử duyệt, tích hợp vận chuyển/cổng thanh toán, chính sách hoàn tiền tự động, audit log đầy đủ hoặc kiểm thử tải. Chưa có tự hết hạn đơn hàng chưa trả tiền; nhân viên cần kiểm tra và hủy đơn bỏ dở để nhả kho. Lượt voucher đã dùng không tự phục hồi khi hủy/hoàn; cần chốt chính sách trước khi thay đổi. Các trường hình ảnh/diễn giả có trong model nhưng chưa có toàn bộ CRUD riêng. SQL Server thực tế, migration/backup/restore và tải đồng thời trên hạ tầng triển khai vẫn là các bước kiểm chứng bắt buộc; không coi test sinh schema SQL là test SQL Server thực.
