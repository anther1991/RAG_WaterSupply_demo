using RAG_WaterSupply.Services;

namespace RAG_WaterSupply.Tests;

public class SqlSafetyValidatorTests
{
    private readonly SqlSafetyValidator _validator = new(["KHACH_HANG", "HOA_DON", "TUYEN_DOC"]);

    [Theory]
    [InlineData("SELECT TOP 200 MaKH, HoTen FROM KHACH_HANG")]
    [InlineData("SELECT COUNT(*) FROM [dbo].[KHACH_HANG];")]
    [InlineData("SELECT k.HoTen, h.TongTien FROM KHACH_HANG k JOIN HOA_DON h ON h.MaKH = k.MaKH")]
    [InlineData("WITH no AS (SELECT MaKH FROM HOA_DON WHERE TrangThai = 'CHUA_THU') SELECT COUNT(*) FROM no")]
    [InlineData("SELECT * FROM KHACH_HANG WHERE MaKH IN (SELECT MaKH FROM HOA_DON WHERE Nam = 2026)")]
    [InlineData("SELECT * FROM KHACH_HANG k, HOA_DON h WHERE h.MaKH = k.MaKH")]
    [InlineData("SELECT * FROM KHACH_HANG WHERE HoTen = N'DROP TABLE; -- chỉ là dữ liệu'")]
    public void Accepts_read_only_queries_on_allowed_tables(string sql)
    {
        var result = _validator.Validate(sql);
        Assert.True(result.IsValid, result.Error);
    }

    [Theory]
    [InlineData("", "rỗng")]
    [InlineData("UPDATE KHACH_HANG SET HoTen = N'x'", "SELECT")]
    [InlineData("DELETE FROM HOA_DON", "SELECT")]
    [InlineData("SELECT * FROM KHACH_HANG; DROP TABLE HOA_DON", "một câu lệnh")]
    [InlineData("SELECT * FROM KHACH_HANG -- bỏ qua phần sau", "comment")]
    [InlineData("SELECT * /* ẩn */ FROM KHACH_HANG", "comment")]
    [InlineData("SELECT * INTO BangMoi FROM KHACH_HANG", "INTO")]
    [InlineData("SELECT * FROM KHACH_HANG WHERE 1 = 1 WAITFOR DELAY '0:0:5'", "WAITFOR")]
    [InlineData("SELECT * FROM OPENROWSET('SQLNCLI', 'Server=x;', 'SELECT 1') AS r", "OPENROWSET")]
    [InlineData("SELECT name FROM sys.tables", "metadata")]
    [InlineData("SELECT * FROM INFORMATION_SCHEMA.COLUMNS", "metadata")]
    [InlineData("SELECT * FROM NguoiDung", "danh sách cho phép")]
    [InlineData("SELECT * FROM audit.NhatKy", "Schema")]
    [InlineData("SELECT * FROM DbKhac.dbo.KHACH_HANG", "")]
    public void Rejects_unsafe_queries(string sql, string expectedErrorPart)
    {
        var result = _validator.Validate(sql);
        Assert.False(result.IsValid);
        Assert.Contains(expectedErrorPart, result.Error, StringComparison.OrdinalIgnoreCase);
    }

    // Join kiểu dấu phẩy: bảng thứ hai cũng phải nằm trong danh sách cho phép
    [Theory]
    [InlineData("SELECT * FROM KHACH_HANG, NguoiDung")]
    [InlineData("SELECT * FROM KHACH_HANG k, dbo.NguoiDung u")]
    [InlineData("SELECT * FROM (SELECT MaKH FROM HOA_DON) x, NguoiDung u")]
    [InlineData("SELECT * FROM KHACH_HANG k CROSS APPLY (SELECT * FROM NguoiDung) u")]
    public void Rejects_tables_hidden_in_comma_joins_or_subqueries(string sql)
    {
        var result = _validator.Validate(sql);
        Assert.False(result.IsValid, $"Lọt qua: {sql}");
    }

    [Theory]
    [InlineData("SELECT * FROM dbo.fn_LayMatKhau()")]
    [InlineData("SELECT * FROM OPENJSON(N'[1,2]')")]
    public void Rejects_table_valued_functions(string sql)
    {
        Assert.False(_validator.Validate(sql).IsValid);
    }
}
