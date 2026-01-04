namespace Centauri_Api.Model
{

    public class ApiResponse<T>
    {
        public bool Success { get; set; }
        public T? Data { get; set; }
        public string? Message { get; set; }
        public ErrorDetails? Error { get; set; }
    }

    public class ErrorDetails
    {
        public string Code { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public int? StatusCode { get; set; }
        public List<FieldError>? Details { get; set; }
    }

    public class FieldError
    {
        public string Field { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }

    public static class ApiResponseHelper
    {
        public static ApiResponse<T> Success<T>(T data, string message = "Success")
        {
            return new ApiResponse<T>
            {
                Success = true,
                Data = data,
                Message = message
            };
        }

        public static ApiResponse<T> Error<T>(string code, string message, int statusCode = 400)
        {
            return new ApiResponse<T>
            {
                Success = false,
                Error = new ErrorDetails
                {
                    Code = code,
                    Message = message,
                    StatusCode = statusCode
                }
            };
        }
    }

}
