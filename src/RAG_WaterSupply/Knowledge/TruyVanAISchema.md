# Database Schema — WaterSupplyDemo

Schema mẫu của một công ty cấp nước giả định. Mỗi bảng là một mục `## TÊN_BẢNG`;
danh sách bảng ở đây cũng chính là danh sách bảng được phép truy vấn (SqlSafetyValidator).

## TUYEN_DOC

| Column Name | Data Type | Max Length | Nullable | Default | Key | Links | Description |
| --- | --- | --- | --- | --- | --- | --- | --- |
| MaTuyen | varchar | 10 | NO |  | PK |  | Mã tuyến đọc số (ví dụ T01) |
| TenTuyen | nvarchar | 100 | NO |  |  |  | Tên tuyến, thường là tên đường chính |
| KhuVuc | nvarchar | 20 | NO |  |  |  | THANH_THI hoặc NONG_THON |

## BANG_GIA

| Column Name | Data Type | Max Length | Nullable | Default | Key | Links | Description |
| --- | --- | --- | --- | --- | --- | --- | --- |
| LoaiGia | varchar | 5 | NO |  | PK |  | Mã loại giá: SH (sinh hoạt), KD (kinh doanh), CQ (cơ quan), SX (sản xuất) |
| TenLoaiGia | nvarchar | 50 | NO |  |  |  | Tên loại giá |
| DonGia | int | NULL | NO |  |  |  | Đơn giá đồng/m³, đã gồm VAT 5% |

## KHACH_HANG

| Column Name | Data Type | Max Length | Nullable | Default | Key | Links | Description |
| --- | --- | --- | --- | --- | --- | --- | --- |
| MaKH | int | NULL | NO |  | PK |  | Mã khách hàng |
| HoTen | nvarchar | 100 | NO |  |  |  | Họ tên khách hàng |
| DiaChi | nvarchar | 200 | NO |  |  |  | Địa chỉ lắp đặt |
| SoDienThoai | varchar | 15 | YES |  |  |  | Số điện thoại liên hệ (dữ liệu cá nhân) |
| MaTuyen | varchar | 10 | NO |  | FK | FK -> [TUYEN_DOC.MaTuyen](#tuyen_doc) | Tuyến đọc số |
| LoaiGia | varchar | 5 | NO |  | FK | FK -> [BANG_GIA.LoaiGia](#bang_gia) | Loại giá áp dụng |
| SoNhanKhau | int | NULL | NO | 1 |  |  | Số nhân khẩu đăng ký |
| TrangThai | varchar | 5 | NO | 'MO' |  |  | MO = đang cấp nước, CUP = đang tạm ngưng |
| NgayLapDat | date | NULL | NO |  |  |  | Ngày bắt đầu sử dụng nước |

## DONG_HO

| Column Name | Data Type | Max Length | Nullable | Default | Key | Links | Description |
| --- | --- | --- | --- | --- | --- | --- | --- |
| SoSerial | varchar | 20 | NO |  | PK |  | Số serial đồng hồ |
| MaKH | int | NULL | NO |  | FK | FK -> [KHACH_HANG.MaKH](#khach_hang) | Khách hàng đang dùng đồng hồ |
| HieuDongHo | nvarchar | 50 | NO |  |  |  | Hiệu đồng hồ |
| CoDongHo | int | NULL | NO |  |  |  | Cỡ đồng hồ (mm) |
| NgayLap | date | NULL | NO |  |  |  | Ngày lắp đồng hồ |
| NgayKiemDinh | date | NULL | YES |  |  |  | Ngày kiểm định gần nhất |

## HOA_DON

| Column Name | Data Type | Max Length | Nullable | Default | Key | Links | Description |
| --- | --- | --- | --- | --- | --- | --- | --- |
| MaHD | int | NULL | NO |  | PK |  | Mã hóa đơn |
| MaKH | int | NULL | NO |  | FK | FK -> [KHACH_HANG.MaKH](#khach_hang) | Khách hàng |
| Thang | int | NULL | NO |  |  |  | Tháng của kỳ hóa đơn (1–12) |
| Nam | int | NULL | NO |  |  |  | Năm của kỳ hóa đơn |
| ChiSoCu | int | NULL | NO |  |  |  | Chỉ số đồng hồ đầu kỳ |
| ChiSoMoi | int | NULL | NO |  |  |  | Chỉ số đồng hồ cuối kỳ |
| TieuThu | int | NULL | NO |  |  |  | Sản lượng tiêu thụ m³ = ChiSoMoi - ChiSoCu |
| TienNuoc | int | NULL | NO |  |  |  | Tiền nước (đã gồm VAT) |
| PhiBVMT | int | NULL | NO |  |  |  | Phí bảo vệ môi trường |
| TongTien | int | NULL | NO |  |  |  | Tổng tiền phải trả |
| TrangThai | varchar | 10 | NO |  |  |  | DA_THU hoặc CHUA_THU |
| NgayThu | date | NULL | YES |  |  |  | Ngày thu tiền (NULL nếu chưa thu) |

## SU_CO

| Column Name | Data Type | Max Length | Nullable | Default | Key | Links | Description |
| --- | --- | --- | --- | --- | --- | --- | --- |
| MaSuCo | int | NULL | NO |  | PK |  | Mã sự cố |
| MaTuyen | varchar | 10 | NO |  | FK | FK -> [TUYEN_DOC.MaTuyen](#tuyen_doc) | Tuyến xảy ra sự cố |
| LoaiSuCo | nvarchar | 50 | NO |  |  |  | Bể ống, rò rỉ, mất áp, chất lượng nước |
| MoTa | nvarchar | 300 | YES |  |  |  | Mô tả |
| ThoiGianBao | datetime | NULL | NO |  |  |  | Thời điểm tiếp nhận |
| ThoiGianXuLy | datetime | NULL | YES |  |  |  | Thời điểm xử lý xong (NULL nếu đang xử lý) |
