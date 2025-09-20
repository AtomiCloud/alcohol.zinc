// using Domain.Stats;

// namespace App.Modules.Stats.Data
// {
//     public static class StatsMapper
//     {
//         public static StatsModel ToDomain(this StatsData data)
//         {
//             if (data == null) return null;
//             return new StatsModel
//             {
//                 Sub = data.Sub,
//                 Date = data.Date,
//                 AmountForDev = data.AmountForDev,
//                 AmountForCharity = data.AmountForCharity
//             };
//         }

//         public static StatsData ToData(this StatsModel model)
//         {
//             if (model == null) return null;
//             return new StatsData
//             {
//                 Sub = model.Sub,
//                 Date = model.Date,
//                 AmountForDev = model.AmountForDev,
//                 AmountForCharity = model.AmountForCharity
//             };
//         }
//     }
// }
