using Refit;

namespace Demo.Libs.Dogs;

public interface IDogsClient
{
    [Get("/api/breeds/image/random")]
    Task<ApiResponse<DogImage>> GetRandomDogImageAsync(CancellationToken cancellationToken);

    [Get("/http404")]
    Task<ApiResponse<DogImage>> FailHttp404Async(CancellationToken cancellationToken);
}