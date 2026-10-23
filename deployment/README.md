# Thử tải và vận hành nhiều instance

Chỉ dùng các script với dữ liệu local/staging có thể xóa. Không stress test hoặc tạo đơn thử trên production.

## Smoke-load đã chạy

`smoke-load.py` dùng Python 3 standard library, không cần k6. Kết quả tại [results/local-smoke.json](results/local-smoke.json): hai process ASP.NET Core Release cùng SQL Server 2022, 1.200 request đọc catalog, concurrency 20, **1.200 trả 200, không lỗi**. Lần đo đạt khoảng 693 request/giây, median 34,09 ms, p95 47,91 ms, p99 63,66 ms. Đây là tải ngắn cục bộ, **không phải SLA hay chứng nhận capacity**.

Đã kiểm chứng đăng nhập/giỏ qua hai process, replay checkout trả cùng order id, hủy lặp không cộng kho lần hai. Hai process quota 5 dùng chung counter trả đúng `200 × 5`, rồi `429 × 3`, có `Retry-After`. Không có tiền hoặc tin nhắn thật được gửi. Bài đo đọc dùng quota staging cao; cặp quota thấp riêng dùng để thử limiter. Không tăng quota production chỉ để cải thiện số liệu tải.

```bash
python3 deployment/smoke-load.py \
  --urls http://127.0.0.1:5081,http://127.0.0.1:5082 \
  --requests 1200 --concurrency 20 --writes \
  --output /tmp/hoasap-smoke.json
```

`--writes` tạo tài khoản QA ngẫu nhiên, đặt/hủy một đơn của sản phẩm còn stock rồi so sánh stock trước/sau. Dữ liệu thử vẫn lưu trong DB. Muốn kiểm tra quota, thêm `--limit-urls` trỏ tới hai process quota 5, cùng DB và window chưa dùng. Các process dùng cùng JWT/key ring và hai giá trị `WindowSeconds`/`AuthWindowSeconds` bằng nhau. `/health` không tiêu thụ quota, vì vậy cũng phải kiểm tra endpoint nghiệp vụ.

## Kịch bản k6 dài hơn

```bash
BASE_URLS=http://127.0.0.1:5081,http://127.0.0.1:5082 \
  VUS=5 DURATION=30s k6 run deployment/k6-ecommerce.js
```

Mặc định chỉ đọc catalog/workshop. Truyền `EMAIL`/`PASSWORD` của tài khoản staging riêng để đo đọc booking. Tạo/hủy đơn trong mỗi vòng lặp chỉ được bật khi chủ động đặt `RUN_WRITES=true` và `PRODUCT_ID` của SKU có stock trên DB dùng thử. Script không thanh toán hoặc gửi tin nhắn. Kịch bản đã cập nhật đúng hợp đồng API; **chưa chạy k6 dài hạn trong đợt QA này**.

Trước go-live, tăng tải dần trên hạ tầng đích và quan sát SQL CPU/I/O, connection pool, deadlock, outbox lag/retry, giữ kho, rate counter, 5xx. Diễn tập mất kết nối/gateway timeout, rolling deploy và phục hồi backup. Đánh giá callback, ghi đơn và cạnh tranh workshop riêng; không suy ra từ tốc độ đọc catalog.

## Backup drill

[results/backup-restore.json](results/backup-restore.json) ghi drill bằng dữ liệu mẫu: SQLite `integrity_check=ok`, tài khoản/đơn/tiền/kho/voucher khớp sau phục hồi, media khớp SHA-256. Chưa drill restore SQL Server/volume của môi trường đích.

Scripts trong `../scripts/`: SQLite dùng backup API nhất quán kể cả WAL; media dùng tar; SQL Server dùng `sqlcmd` và đường dẫn backup ở phía server. Restore có xác nhận rõ ràng, SQLite yêu cầu ứng dụng đã dừng, media yêu cầu thư mục trống và từ chối path/link nguy hiểm. Scripts không tự cài lịch backup. DB/media/key ring cần cùng snapshot, bản sao ngoài máy chủ được mã hóa, retention được chọn theo nhu cầu, kiểm tra quyền và health trước mở lại traffic. Xem [runbook](../docs/operations.md).
