using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace App.Modules.Completion.Data
{
    public class CompletionData
    {
        [Key, Column(Order = 0)]
        public DateOnly Date { get; set; }
        [Key, Column(Order = 1)]
        public int TaskId { get; set; }
    }
}
