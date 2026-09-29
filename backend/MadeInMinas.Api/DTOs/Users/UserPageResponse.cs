namespace MadeInMinas.Api.DTOs.Users;

public sealed record UserPageResponse(UserResponse[] Items, int Page, int PageSize, int TotalCount);
