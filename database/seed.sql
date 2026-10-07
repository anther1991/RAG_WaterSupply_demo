/* =========================================================================
   WaterSupplyDemo — CSDL mẫu cho RAG WaterSupply (dữ liệu hoàn toàn giả lập)
   Chạy bằng sqlcmd (cần biến RAG_READER_PASSWORD), ví dụ:
     sqlcmd -S localhost,1433 -U sa -P "<sa password>" -C -i database/seed.sql -v RAG_READER_PASSWORD="<mật khẩu mạnh>"
   ========================================================================= */
IF DB_ID('WaterSupplyDemo') IS NULL CREATE DATABASE WaterSupplyDemo;
GO
USE WaterSupplyDemo;
GO

DROP TABLE IF EXISTS SU_CO, HOA_DON, DONG_HO, KHACH_HANG, BANG_GIA, TUYEN_DOC;
GO

CREATE TABLE TUYEN_DOC (
    MaTuyen  varchar(10)   NOT NULL PRIMARY KEY,
    TenTuyen nvarchar(100) NOT NULL,
    KhuVuc   nvarchar(20)  NOT NULL
);
CREATE TABLE BANG_GIA (
    LoaiGia    varchar(5)   NOT NULL PRIMARY KEY,
    TenLoaiGia nvarchar(50) NOT NULL,
    DonGia     int          NOT NULL
);
CREATE TABLE KHACH_HANG (
    MaKH        int           NOT NULL PRIMARY KEY,
    HoTen       nvarchar(100) NOT NULL,
    DiaChi      nvarchar(200) NOT NULL,
    SoDienThoai varchar(15)   NULL,
    MaTuyen     varchar(10)   NOT NULL REFERENCES TUYEN_DOC(MaTuyen),
    LoaiGia     varchar(5)    NOT NULL REFERENCES BANG_GIA(LoaiGia),
    SoNhanKhau  int           NOT NULL DEFAULT 1,
    TrangThai   varchar(5)    NOT NULL DEFAULT 'MO',
    NgayLapDat  date          NOT NULL
);
CREATE TABLE DONG_HO (
    SoSerial     varchar(20)  NOT NULL PRIMARY KEY,
    MaKH         int          NOT NULL REFERENCES KHACH_HANG(MaKH),
    HieuDongHo   nvarchar(50) NOT NULL,
    CoDongHo     int          NOT NULL,
    NgayLap      date         NOT NULL,
    NgayKiemDinh date         NULL
);
CREATE TABLE HOA_DON (
    MaHD      int         NOT NULL PRIMARY KEY,
    MaKH      int         NOT NULL REFERENCES KHACH_HANG(MaKH),
    Thang     int         NOT NULL,
    Nam       int         NOT NULL,
    ChiSoCu   int         NOT NULL,
    ChiSoMoi  int         NOT NULL,
    TieuThu   int         NOT NULL,
    TienNuoc  int         NOT NULL,
    PhiBVMT   int         NOT NULL,
    TongTien  int         NOT NULL,
    TrangThai varchar(10) NOT NULL,
    NgayThu   date        NULL
);
CREATE TABLE SU_CO (
    MaSuCo       int           NOT NULL PRIMARY KEY,
    MaTuyen      varchar(10)   NOT NULL REFERENCES TUYEN_DOC(MaTuyen),
    LoaiSuCo     nvarchar(50)  NOT NULL,
    MoTa         nvarchar(300) NULL,
    ThoiGianBao  datetime      NOT NULL,
    ThoiGianXuLy datetime      NULL
);
GO

INSERT INTO TUYEN_DOC VALUES
 ('T01', N'Đường Số 1',     N'THANH_THI'), ('T02', N'Đường Số 2',   N'THANH_THI'),
 ('T03', N'Đường Hoa Sen',  N'THANH_THI'), ('T04', N'Đường Bờ Sông', N'THANH_THI'),
 ('T05', N'Ấp Tân Phú',     N'NONG_THON'), ('T06', N'Ấp Mỹ Hòa',    N'NONG_THON');

INSERT INTO BANG_GIA VALUES
 ('SH', N'Sinh hoạt',  8500), ('KD', N'Kinh doanh', 15500),
 ('CQ', N'Cơ quan',   12000), ('SX', N'Sản xuất',   11000);
GO

/* 300 khách hàng — tên ghép từ các danh sách cố định, dữ liệu sinh theo công thức nên chạy lại vẫn ra cùng kết quả */
;WITH n AS (SELECT TOP (300) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS i FROM sys.all_objects),
ho  AS (SELECT * FROM (VALUES (0,N'Nguyễn'),(1,N'Trần'),(2,N'Lê'),(3,N'Phạm'),(4,N'Huỳnh'),(5,N'Võ'),(6,N'Đặng'),(7,N'Bùi')) v(k, s)),
dem AS (SELECT * FROM (VALUES (0,N'Văn'),(1,N'Thị'),(2,N'Minh'),(3,N'Ngọc'),(4,N'Hữu'),(5,N'Thanh')) v(k, s)),
ten AS (SELECT * FROM (VALUES (0,N'An'),(1,N'Bình'),(2,N'Châu'),(3,N'Dũng'),(4,N'Hà'),(5,N'Khoa'),(6,N'Lan'),(7,N'Nam'),(8,N'Phúc'),(9,N'Trang'),(10,N'Vy')) v(k, s))
INSERT INTO KHACH_HANG (MaKH, HoTen, DiaChi, SoDienThoai, MaTuyen, LoaiGia, SoNhanKhau, TrangThai, NgayLapDat)
SELECT 100000 + n.i,
       UPPER(ho.s + N' ' + dem.s + N' ' + ten.s),
       CONCAT(n.i % 120 + 1, N'/', n.i % 9 + 1, N' ', t.TenTuyen),
       CONCAT('0900', RIGHT('000000' + CAST(n.i AS varchar(6)), 6)),
       t.MaTuyen,
       CASE WHEN n.i % 10 = 0 THEN 'KD' WHEN n.i % 29 = 0 THEN 'CQ' WHEN n.i % 37 = 0 THEN 'SX' ELSE 'SH' END,
       1 + n.i % 6,
       CASE WHEN n.i % 25 = 0 THEN 'CUP' ELSE 'MO' END,
       DATEADD(DAY, -(n.i * 13 % 3000), '2026-09-01')
FROM n
JOIN ho  ON ho.k  = n.i % 8
JOIN dem ON dem.k = (n.i / 8) % 6
JOIN ten ON ten.k = (n.i * 7) % 11
JOIN TUYEN_DOC t ON t.MaTuyen = CONCAT('T0', n.i % 6 + 1);

INSERT INTO DONG_HO (SoSerial, MaKH, HieuDongHo, CoDongHo, NgayLap, NgayKiemDinh)
SELECT CONCAT('SN', MaKH), MaKH,
       CASE MaKH % 3 WHEN 0 THEN N'Hiệu A' WHEN 1 THEN N'Hiệu B' ELSE N'Hiệu C' END,
       CASE WHEN LoaiGia = 'SH' THEN 15 ELSE 25 END,
       NgayLapDat,
       CASE WHEN MaKH % 7 = 0 THEN NULL ELSE DATEADD(DAY, MaKH % 2200, NgayLapDat) END
FROM KHACH_HANG;
GO

/* 12 kỳ hóa đơn: 10/2025 → 9/2026 */
;WITH ky AS (SELECT TOP (12) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 1 AS k FROM sys.all_objects),
tt AS (
    SELECT kh.MaKH, kh.LoaiGia, kh.TrangThai,
           MONTH(DATEADD(MONTH, ky.k, '2025-10-01')) AS Thang,
           YEAR(DATEADD(MONTH, ky.k, '2025-10-01'))  AS Nam,
           ky.k,
           (6 + kh.SoNhanKhau * 3 + (kh.MaKH * 7 + ky.k * 13) % 9)
             * CASE kh.LoaiGia WHEN 'SH' THEN 1 ELSE 3 END AS TieuThu
    FROM KHACH_HANG kh CROSS JOIN ky
),
cs AS (
    SELECT tt.*,
           (tt.MaKH * 37) % 900
             + SUM(tt.TieuThu) OVER (PARTITION BY tt.MaKH ORDER BY tt.k ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING) AS ChiSoCuRaw
    FROM tt
)
INSERT INTO HOA_DON (MaHD, MaKH, Thang, Nam, ChiSoCu, ChiSoMoi, TieuThu, TienNuoc, PhiBVMT, TongTien, TrangThai, NgayThu)
SELECT ROW_NUMBER() OVER (ORDER BY cs.k, cs.MaKH),
       cs.MaKH, cs.Thang, cs.Nam,
       ISNULL(cs.ChiSoCuRaw, (cs.MaKH * 37) % 900),
       ISNULL(cs.ChiSoCuRaw, (cs.MaKH * 37) % 900) + cs.TieuThu,
       cs.TieuThu,
       cs.TieuThu * g.DonGia,
       ROUND(cs.TieuThu * g.DonGia / 1.05 * 0.1, 0),
       cs.TieuThu * g.DonGia + ROUND(cs.TieuThu * g.DonGia / 1.05 * 0.1, 0),
       x.TrangThai,
       CASE WHEN x.TrangThai = 'DA_THU' THEN DATEADD(DAY, 3 + cs.MaKH % 12, DATEFROMPARTS(cs.Nam, cs.Thang, 15)) END
FROM cs
JOIN BANG_GIA g ON g.LoaiGia = cs.LoaiGia
CROSS APPLY (SELECT CASE
    WHEN cs.k = 11 AND cs.MaKH % 3 = 0 THEN 'CHUA_THU'          -- kỳ mới nhất: khoảng 1/3 chưa thu
    WHEN cs.TrangThai = 'CUP' AND cs.k >= 9 THEN 'CHUA_THU'     -- khách bị tạm ngưng còn nợ vài kỳ
    WHEN cs.MaKH % 41 = 0 AND cs.k >= 10 THEN 'CHUA_THU'
    ELSE 'DA_THU' END AS TrangThai) x;
GO

INSERT INTO SU_CO (MaSuCo, MaTuyen, LoaiSuCo, MoTa, ThoiGianBao, ThoiGianXuLy) VALUES
 (1, 'T01', N'Bể ống',          N'Bể ống phân phối D100 trước số 12',   '2026-07-03 08:15', '2026-07-03 11:40'),
 (2, 'T03', N'Rò rỉ',           N'Rò rỉ tại hộp đồng hồ',               '2026-07-19 14:02', '2026-07-20 09:10'),
 (3, 'T05', N'Mất áp',          N'Áp lực yếu giờ cao điểm',             '2026-08-02 17:30', '2026-08-03 10:00'),
 (4, 'T02', N'Chất lượng nước', N'Nước đục sau sửa chữa',               '2026-08-21 07:45', '2026-08-21 12:20'),
 (5, 'T06', N'Bể ống',          N'Bể ống nhánh qua cầu',                '2026-09-09 21:05', '2026-09-10 02:30'),
 (6, 'T04', N'Rò rỉ',           N'Rò rỉ mối nối đầu hẻm',               '2026-09-27 09:20', NULL),
 (7, 'T01', N'Mất áp',          N'Áp lực yếu do súc xả đường ống',      '2026-09-30 15:10', NULL);
GO

/* Tài khoản chỉ đọc cho ứng dụng: chỉ SELECT trên đúng các bảng trong schema (lớp bảo vệ cuối, ngoài SqlSafetyValidator) */
USE master;
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = 'rag_reader')
    CREATE LOGIN rag_reader WITH PASSWORD = '$(RAG_READER_PASSWORD)', CHECK_POLICY = ON;
GO
USE WaterSupplyDemo;
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = 'rag_reader')
    CREATE USER rag_reader FOR LOGIN rag_reader;
GRANT SELECT ON dbo.TUYEN_DOC  TO rag_reader;
GRANT SELECT ON dbo.BANG_GIA   TO rag_reader;
GRANT SELECT ON dbo.KHACH_HANG TO rag_reader;
GRANT SELECT ON dbo.DONG_HO    TO rag_reader;
GRANT SELECT ON dbo.HOA_DON    TO rag_reader;
GRANT SELECT ON dbo.SU_CO      TO rag_reader;
GO
