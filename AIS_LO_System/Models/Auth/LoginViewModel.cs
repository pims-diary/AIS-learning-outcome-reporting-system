using System.ComponentModel.DataAnnotations;

namespace LOARS.Web.Models.Auth
{
    public class LoginViewModel
    {
        [Required]
        public string Username { get; set; } = "";

        [Required]
        [DataType(DataType.Password)]
        public string Password { get; set; } = "";
    }
}
