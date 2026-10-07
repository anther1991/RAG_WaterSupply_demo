using System.Runtime.CompilerServices;
using System.Text.Json;
using OllamaSharp;
using OllamaSharp.Models;
using RAG_WaterSupply.Models;

namespace RAG_WaterSupply.Services;

public sealed class AnswerComposer(OllamaApiClient client, string model)
{
    public IAsyncEnumerable<string> StreamKnowledgeAnswerAsync(
        string question,
        IReadOnlyList<ChatMessage> history,
        string knowledge,
        CancellationToken cancellationToken = default)
    {
        var prompt = $$"""
        Bạn là trợ lý nội bộ của Công ty Cấp nước Demo.
        Nhiệm vụ của bạn là trả lời câu hỏi bằng tiếng Việt một cách chính xác, thân thiện.

        Nguồn thông tin của bạn gồm có:
        1. KIẾN THỨC LIÊN QUAN: Các tài liệu chính thống được hệ thống truy xuất từ cơ sở dữ liệu tri thức của công ty (nằm ở mục tương ứng bên dưới).
        2. HÌNH ẢNH CỦA NGƯỜI DÙNG: Nếu người dùng có gửi kèm hình ảnh, hệ thống đã tự động phân tích và chuyển đổi hình ảnh đó thành văn bản mô tả chi tiết nằm trong câu hỏi dưới dạng `[Thông tin từ hình ảnh: ...]`. Bạn PHẢI coi phần văn bản mô tả này chính là nội dung trực quan của bức ảnh mà người dùng đã tải lên. Hãy phân tích, giải thích, tóm tắt hoặc trích xuất dữ liệu từ phần mô tả ảnh này để trả lời trực tiếp và đầy đủ cho người dùng. Tuyệt đối không được yêu cầu người dùng gửi lại ảnh hay báo rằng không tìm thấy ảnh.

        Quy tắc trả lời:
        - Nếu câu hỏi hỏi về hình ảnh đã tải lên, hãy ưu tiên dùng thông tin từ "HÌNH ẢNH CỦA NGƯỜI DÙNG" để giải thích và trả lời trực tiếp.
        - Chỉ trả lời dựa trên 2 nguồn thông tin trên. Nếu không tìm thấy câu trả lời trong các nguồn trên, bạn bắt buộc phải bắt đầu câu trả lời ngay lập tức bằng ký hiệu [NOT_FOUND] (không viết gì khác trước ký hiệu này), sau đó mới viết lời giải thích lịch sự rằng không tìm thấy thông tin.
        - Định dạng câu trả lời bằng Markdown; dùng bảng Markdown khi cần so sánh.
        Công thức toán dùng $...$ hoặc $$...$$ theo cú pháp LaTeX. Không tạo HTML trực tiếp.
        Đặt nhãn tiếng Việt bên ngoài công thức; bên trong LaTeX chỉ dùng ký hiệu, số và tên biến ASCII.
        Nếu câu trả lời mô tả quy trình, luồng xử lý hoặc mối quan hệ phức tạp, hãy vẽ sơ đồ bằng Mermaid.js sử dụng khối mã ```mermaid ... ```. Sơ đồ Mermaid nên dùng định dạng flowchart TD hoặc flowchart LR. Chỉ dùng nhãn tiếng Việt hoặc ký tự ASCII chuẩn bên trong các hộp sơ đồ.

        LỊCH SỬ GẦN ĐÂY:
        {{PromptHelpers.BuildHistory(history)}}

        KIẾN THỨC LIÊN QUAN:
        {{knowledge}}

        CÂU HỎI: {{question}}
        """;

        return StreamAsync(prompt, cancellationToken);
    }

    public IAsyncEnumerable<string> StreamDatabaseAnswerAsync(
        string question,
        IReadOnlyList<ChatMessage> history,
        DatabaseResult result,
        string? additionalKnowledge,
        CancellationToken cancellationToken = default)
    {
        var resultJson = JsonSerializer.Serialize(result.Rows, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });

        var prompt = $$"""
        Bạn là trợ lý nội bộ của Công ty Cấp nước Demo.
        Hãy trả lời trực tiếp câu hỏi bằng tiếng Việt dựa trên kết quả database bên dưới.
        Nếu trong CÂU HỎI có đính kèm thông tin hình ảnh (nhãn `[Thông tin từ hình ảnh: ...]`), bạn có thể sử dụng thông tin từ ảnh đó kết hợp với kết quả database để so sánh, đối chiếu hoặc làm rõ câu trả lời cho người dùng.
        Không nhắc lại SQL. Không thêm số liệu không có trong kết quả.
        Nếu mảng kết quả rỗng, thông báo không tìm thấy dữ liệu.
        Định dạng câu trả lời bằng Markdown; dùng bảng Markdown khi việc so sánh sẽ rõ hơn.
        Công thức toán dùng $...$ hoặc $$...$$ theo cú pháp LaTeX. Không tạo HTML trực tiếp.
        Đặt nhãn tiếng Việt bên ngoài công thức; bên trong LaTeX chỉ dùng ký hiệu, số và tên biến ASCII.
        Nếu câu trả lời mô tả quy trình, luồng xử lý hoặc mối quan hệ phức tạp, hãy vẽ sơ đồ bằng Mermaid.js sử dụng khối mã ```mermaid ... ```. Sơ đồ Mermaid nên dùng định dạng flowchart TD hoặc flowchart LR. Chỉ dùng nhãn tiếng Việt hoặc ký tự ASCII chuẩn bên trong các hộp sơ đồ.

        LỊCH SỬ GẦN ĐÂY:
        {{PromptHelpers.BuildHistory(history)}}

        KIẾN THỨC BỔ SUNG (nếu có):
        {{additionalKnowledge}}

        CÂU HỎI: {{question}}

        KẾT QUẢ DATABASE:
        {{resultJson}}
        """;

        return StreamAsync(prompt, cancellationToken);
    }

    public IAsyncEnumerable<string> StreamConversationAnswerAsync(
        string question,
        IReadOnlyList<ChatMessage> history,
        CancellationToken cancellationToken = default)
    {
        var prompt = $$"""
        Bạn là trợ lý AI thân thiện của Công ty Cấp nước Demo.
        Hãy trò chuyện xã giao, trả lời câu hỏi của người dùng một cách tự nhiên, thân thiện và ngắn gọn (dưới 3 câu).
        Nếu người dùng hỏi bạn là ai hoặc có thể làm gì, hãy giới thiệu bạn là trợ lý hỗ trợ về sổ tay dịch vụ và dữ liệu khách hàng của công ty.
        Không nhắc lại các cấu trúc kỹ thuật hay mã lệnh. Trả lời bằng tiếng Việt.

        LỊCH SỬ GẦN ĐÂY:
        {{PromptHelpers.BuildHistory(history)}}

        CÂU HỎI XÃ GIAO: {{question}}
        """;

        return StreamAsync(prompt, cancellationToken);
    }

    private async IAsyncEnumerable<string> StreamAsync(
        string prompt,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var part in client.GenerateAsync(new GenerateRequest
        {
            Prompt = prompt,
            Model = model,
            Stream = true
        }, cancellationToken))
        {
            if (part?.Response is { Length: > 0 } response)
            {
                yield return response;
            }
        }
    }
}
