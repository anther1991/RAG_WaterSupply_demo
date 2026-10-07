# QUY TẮC NGHIỆP VỤ — Công ty Cấp nước Demo

## Quy tắc chung
- Các quy tắc ở đây hướng dẫn cách tìm câu trả lời; không cần liệt kê lại trong câu trả lời.
- Luôn đối chiếu schema để dùng đúng tên bảng, tên cột.
- Chỉ đọc dữ liệu. Không sửa, xóa, thêm dữ liệu.
- Dùng JOIN tường minh (INNER JOIN / LEFT JOIN … ON …), không join bằng dấu phẩy.
- Không trả về SoDienThoai, trừ khi người dùng hỏi trực tiếp số điện thoại của một khách hàng cụ thể.

## Khách hàng
- "Số khách hàng" = số dòng trong KHACH_HANG.
- Khách hàng đang sử dụng nước: KHACH_HANG.TrangThai = 'MO'. Đang tạm ngưng (cúp nước): TrangThai = 'CUP'.
- Khu vực của khách hàng lấy từ TUYEN_DOC.KhuVuc thông qua KHACH_HANG.MaTuyen.

## Hóa đơn và sản lượng
- Kỳ hóa đơn xác định bằng cặp (Thang, Nam). "Tháng 9/2026" nghĩa là Thang = 9 AND Nam = 2026.
- Sản lượng (tiêu thụ) của một kỳ = SUM(HOA_DON.TieuThu) của kỳ đó, đơn vị m³.
- Doanh thu phát sinh của một kỳ = SUM(HOA_DON.TongTien). Doanh thu đã thu = SUM(TongTien) với TrangThai = 'DA_THU'.
- Khách hàng còn nợ: có ít nhất một hóa đơn TrangThai = 'CHUA_THU'. Số tiền nợ = SUM(TongTien) của các hóa đơn đó.
- Khi so sánh hai kỳ, trả về từng kỳ và chênh lệch (kỳ sau trừ kỳ trước).

## Đồng hồ nước
- Đồng hồ cần kiểm định lại khi NgayKiemDinh cách ngày hiện tại hơn 5 năm, hoặc NgayKiemDinh IS NULL.

## Sự cố
- Sự cố đang xử lý: SU_CO.ThoiGianXuLy IS NULL.
- Thời gian xử lý (giờ) = DATEDIFF(HOUR, ThoiGianBao, ThoiGianXuLy).
