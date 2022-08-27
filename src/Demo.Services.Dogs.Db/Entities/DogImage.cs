using System.ComponentModel.DataAnnotations;

namespace Demo.Services.Dogs.Db.Entities;

public record DogImage
{
    [Key]
    public int Id { get; set; }
    
    public string Url { get; set; }
}