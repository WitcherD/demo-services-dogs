using Refit;

namespace Demo.Libs.Dogs;

public interface IDogsClient
{
    [Get("/api/breeds/image/random")]
    Task<ApiResponse<DogImage>> GetRandomDogImageAsync(CancellationToken cancellationToken);

    [Get("/http500")]
    Task<ApiResponse<DogImage>> FailHttp500Async(CancellationToken cancellationToken);
}