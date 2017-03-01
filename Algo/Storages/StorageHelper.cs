#region S# License
/******************************************************************************************
NOTICE!!!  This program and source code is owned and licensed by
StockSharp, LLC, www.stocksharp.com
Viewing or use of this code requires your acceptance of the license
agreement found at https://github.com/StockSharp/StockSharp/blob/master/LICENSE
Removal of this comment is a violation of the license agreement.

Project: StockSharp.Algo.Storages.Algo
File: StorageHelper.cs
Created: 2015, 11, 11, 2:32 PM

Copyright 2010 by StockSharp, LLC
*******************************************************************************************/
#endregion S# License
namespace StockSharp.Algo.Storages
{
	using System;
	using System.Collections;
	using System.Collections.Generic;
	using System.Diagnostics;
	using System.IO;
	using System.Linq;

	using Ecng.Collections;
	using Ecng.Common;
	using Ecng.ComponentModel;
	using Ecng.Configuration;
	using Ecng.Interop;

	using StockSharp.Algo.Candles;
	using StockSharp.BusinessEntities;
	using StockSharp.Localization;
	using StockSharp.Logging;
	using StockSharp.Messages;

	/// <summary>
	/// Extension class for storage.
	/// </summary>
	public static class StorageHelper
	{
		private sealed class RangeEnumerable<TData> : SimpleEnumerable<TData>//, IEnumerableEx<TData>
		{
			[DebuggerDisplay("From {_from} Cur {_currDate} To {_to}")]
			private sealed class RangeEnumerator : IEnumerator<TData>
			{
				private DateTime _currDate;
				private readonly IMarketDataStorage<TData> _storage;
				private readonly DateTime _from;
				private readonly DateTime _to;
				private readonly Func<TData, DateTimeOffset> _getTime;
				private IEnumerator<TData> _current;

				private bool _checkBounds;
				private readonly Range<DateTime> _bounds;

				public RangeEnumerator(IMarketDataStorage<TData> storage, DateTimeOffset from, DateTimeOffset to, Func<TData, DateTimeOffset> getTime)
				{
					_storage = storage;
					_from = from.UtcDateTime;
					_to = to.UtcDateTime;
					_getTime = getTime;
					_currDate = from.UtcDateTime.Date;

					_checkBounds = true; // проверяем нижнюю границу
					_bounds = new Range<DateTime>(_from, _to);
				}

				void IDisposable.Dispose()
				{
					Reset();
				}

				bool IEnumerator.MoveNext()
				{
					if (_current == null)
					{
						_current = _storage.Load(_currDate).GetEnumerator();
					}

					while (true)
					{
						if (!_current.MoveNext())
						{
							_current.Dispose();

							var canMove = false;

							while (!canMove)
							{
								_currDate += TimeSpan.FromDays(1);

								if (_currDate > _to)
									break;

								_checkBounds = _currDate == _to.Date;

								_current = _storage.Load(_currDate).GetEnumerator();

								canMove = _current.MoveNext();
							}

							if (!canMove)
								return false;
						}

						if (!_checkBounds)
							break;

						do
						{
							var time = _getTime(Current).UtcDateTime;

							if (_bounds.Contains(time))
								return true;

							if (time > _to)
								return false;
						}
						while (_current.MoveNext());
					}

					return true;
				}

				public void Reset()
				{
					if (_current != null)
					{
						_current.Dispose();
						_current = null;
					}

					_checkBounds = true;
					_currDate = _from.Date;
				}

				public TData Current => _current.Current;

				object IEnumerator.Current => Current;
			}

			//private readonly IMarketDataStorage<TData> _storage;
			//private readonly DateTimeOffset _from;
			//private readonly DateTimeOffset _to;

			public RangeEnumerable(IMarketDataStorage<TData> storage, DateTimeOffset from, DateTimeOffset to, Func<TData, DateTimeOffset> getTime)
				: base(() => new RangeEnumerator(storage, from, to, getTime))
			{
				if (storage == null)
					throw new ArgumentNullException(nameof(storage));

				if (getTime == null)
					throw new ArgumentNullException(nameof(getTime));

				if (from > to)
					throw new ArgumentOutOfRangeException(nameof(@from));

				//_storage = storage;
				//_from = from;
				//_to = to;
			}

			//private int? _count;

			//int IEnumerableEx.Count
			//{
			//	get
			//	{
			//		if (_count == null)
			//		{
			//			// TODO
			//			//if (_from.TimeOfDay != TimeSpan.Zero || _to.TimeOfDay != TimeSpan.Zero)
			//			//	throw new InvalidOperationException("Невозможно вычислить количество элементов для диапазона со временем. Можно использовать только диапазон по датами.");

			//			var count = 0;

			//			for (var i = _from; i <= _to; i += TimeSpan.FromDays(1))
			//				count += _storage.Load(i.UtcDateTime).Count;

			//			_count = count;
			//		}

			//		return (int)_count;
			//	}
			//}
		}

		/// <summary>
		/// To get the storage of candles.
		/// </summary>
		/// <typeparam name="TCandle">The candle type.</typeparam>
		/// <typeparam name="TArg">The type of candle parameter.</typeparam>
		/// <param name="storageRegistry">The external storage.</param>
		/// <param name="security">Security.</param>
		/// <param name="arg">Candle arg.</param>
		/// <param name="drive">The storage. If a value is <see langword="null" />, <see cref="IStorageRegistry.DefaultDrive"/> will be used.</param>
		/// <param name="format">The format type. By default <see cref="StorageFormats.Binary"/> is passed.</param>
		/// <returns>The candles storage.</returns>
		public static IMarketDataStorage<Candle> GetCandleStorage<TCandle, TArg>(this IStorageRegistry storageRegistry, Security security, TArg arg, IMarketDataDrive drive = null, StorageFormats format = StorageFormats.Binary)
			where TCandle : Candle
		{
			return storageRegistry.ThrowIfNull().GetCandleStorage(typeof(TCandle), security, arg, drive, format);
		}

		/// <summary>
		/// To get the storage of candles.
		/// </summary>
		/// <param name="storageRegistry">The external storage.</param>
		/// <param name="series">Candles series.</param>
		/// <param name="drive">The storage. If a value is <see langword="null" />, <see cref="IStorageRegistry.DefaultDrive"/> will be used.</param>
		/// <param name="format">The format type. By default <see cref="StorageFormats.Binary"/> is passed.</param>
		/// <returns>The candles storage.</returns>
		public static IMarketDataStorage<Candle> GetCandleStorage(this IStorageRegistry storageRegistry, CandleSeries series, IMarketDataDrive drive = null, StorageFormats format = StorageFormats.Binary)
		{
			if (series == null)
				throw new ArgumentNullException(nameof(series));

			return storageRegistry.ThrowIfNull().GetCandleStorage(series.CandleType, series.Security, series.Arg, drive, format);
		}

		private static IStorageRegistry ThrowIfNull(this IStorageRegistry storageRegistry)
		{
			if (storageRegistry == null)
				throw new ArgumentNullException(nameof(storageRegistry));

			return storageRegistry;
		}

		internal static IEnumerable<Range<DateTimeOffset>> GetRanges<TValue>(this IMarketDataStorage<TValue> storage)
		{
			if (storage == null)
				throw new ArgumentNullException(nameof(storage));

			var range = GetRange(storage, null, null);

			if (range == null)
				return Enumerable.Empty<Range<DateTimeOffset>>();

			return storage.Dates.Select(d => d.ApplyTimeZone(TimeZoneInfo.Utc)).GetRanges(range.Min, range.Max);
		}

		/// <summary>
		/// To create an iterative loader of market data for the time range.
		/// </summary>
		/// <typeparam name="TData">Data type.</typeparam>
		/// <param name="storage">Market-data storage.</param>
		/// <param name="from">The start time for data loading. If the value is not specified, data will be loaded from the starting time <see cref="GetFromDate"/>.</param>
		/// <param name="to">The end time for data loading. If the value is not specified, data will be loaded up to the <see cref="GetToDate"/> date, inclusive.</param>
		/// <returns>The iterative loader of market data.</returns>
		public static IEnumerable<TData> Load<TData>(this IMarketDataStorage<TData> storage, DateTimeOffset? from = null, DateTimeOffset? to = null)
		{
			var range = GetRange(storage, from, to);

			return range == null
				? Enumerable.Empty<TData>()
				: new RangeEnumerable<TData>(storage, range.Min, range.Max, ((IMarketDataStorageInfo<TData>)storage).GetTime);
		}

		/// <summary>
		/// To delete market data from the storage for the specified time period.
		/// </summary>
		/// <param name="storage">Market-data storage.</param>
		/// <param name="from">The start time for data deleting. If the value is not specified, the data will be deleted starting from the date <see cref="GetFromDate"/>.</param>
		/// <param name="to">The end time, up to which the data shall be deleted. If the value is not specified, data will be deleted up to the end date <see cref="GetToDate"/>, inclusive.</param>
		public static void Delete(this IMarketDataStorage storage, DateTimeOffset? from = null, DateTimeOffset? to = null)
		{
			if (storage == null)
				throw new ArgumentNullException(nameof(storage));

			var range = GetRange(storage, from, to);

			if (range == null)
				return;

			var info = (IMarketDataStorageInfo)storage;

			var min = range.Min.UtcDateTime;
			var max = range.Max.UtcDateTime.EndOfDay();

			for (var date = min; date <= max; date = date.AddDays(1))
			{
				if (date == min)
				{
					var metaInfo = storage.GetMetaInfo(date.Date);

					if (metaInfo == null)
						continue;

					if (metaInfo.FirstTime >= date && max.Date != min.Date)
					{
						storage.Delete(date.Date);
					}
					else
					{
						var data = storage.Load(date.Date).Cast<object>().ToList();
						data.RemoveWhere(d =>
						{
							var time = info.GetTime(d);
							return time.UtcDateTime < min || time > range.Max;
						});
						storage.Delete(data);
					}
				}
				else if (date.Date < max.Date)
					storage.Delete(date.Date);
				else
				{
					var data = storage.Load(date.Date).Cast<object>().ToList();
					data.RemoveWhere(d => info.GetTime(d) > range.Max);
					storage.Delete(data);
				}
			}
		}

		internal static Range<DateTimeOffset> GetRange(this IMarketDataStorage storage, DateTimeOffset? from, DateTimeOffset? to)
		{
			if (storage == null)
				throw new ArgumentNullException(nameof(storage));

			if (from > to)
				throw new ArgumentOutOfRangeException(nameof(to), to, LocalizedStrings.Str1014);

			var dates = storage.Dates.ToArray();

			if (dates.IsEmpty())
				return null;

			var first = dates.First().ApplyTimeZone(TimeZoneInfo.Utc);
			var last = dates.Last().EndOfDay().ApplyTimeZone(TimeZoneInfo.Utc);

			return new Range<DateTimeOffset>(first, last).Intersect(new Range<DateTimeOffset>((from ?? first).Truncate(), (to ?? last).Truncate()));
		}

		/// <summary>
		/// To get the start date for market data, stored in the storage.
		/// </summary>
		/// <param name="storage">Market-data storage.</param>
		/// <returns>The start date. If the value is not initialized, the storage is empty.</returns>
		public static DateTime? GetFromDate(this IMarketDataStorage storage)
		{
			return storage.Dates.FirstOr();
		}

		/// <summary>
		/// To get the end date for market data, stored in the storage.
		/// </summary>
		/// <param name="storage">Market-data storage.</param>
		/// <returns>The end date. If the value is not initialized, the storage is empty.</returns>
		public static DateTime? GetToDate(this IMarketDataStorage storage)
		{
			return storage.Dates.LastOr();
		}

		/// <summary>
		/// To get all dates for stored market data for the specified range.
		/// </summary>
		/// <param name="storage">Market-data storage.</param>
		/// <param name="from">The range start time. If the value is not specified, data will be loaded from the start date <see cref="GetFromDate"/>.</param>
		/// <param name="to">The range end time. If the value is not specified, data will be loaded up to the end date <see cref="GetToDate"/>, inclusive.</param>
		/// <returns>All available data within the range.</returns>
		public static IEnumerable<DateTime> GetDates(this IMarketDataStorage storage, DateTime? from, DateTime? to)
		{
			var dates = storage.Dates;

			if (from != null)
				dates = dates.Where(d => d >= from.Value);

			if (to != null)
				dates = dates.Where(d => d <= to.Value);

			return dates;
		}

		/// <summary>
		/// To convert string representation of the candle argument into typified.
		/// </summary>
		/// <param name="messageType">The type of candle message.</param>
		/// <param name="str">The string representation of the argument.</param>
		/// <returns>Argument.</returns>
		public static object ToCandleArg(this Type messageType, string str)
		{
			if (messageType == null)
				throw new ArgumentNullException(nameof(messageType));

			if (str.IsEmpty())
				throw new ArgumentNullException(nameof(str));

			if (messageType == typeof(TimeFrameCandleMessage))
			{
				return str.Replace('-', ':').To<TimeSpan>();
			}
			else if (messageType == typeof(TickCandleMessage))
			{
				return str.To<int>();
			}
			else if (messageType == typeof(VolumeCandleMessage))
			{
				return str.To<decimal>();
			}
			else if (messageType == typeof(RangeCandleMessage) || messageType == typeof(RenkoCandleMessage))
			{
				return str.To<Unit>();
			}
			else if (messageType == typeof(PnFCandleMessage))
			{
				return str.To<PnFArg>();
			}
			else
				throw new ArgumentOutOfRangeException(nameof(messageType), messageType, LocalizedStrings.WrongCandleType);
		}

		/// <summary>
		/// Read instrument by indentifier.
		/// </summary>
		/// <param name="securities">Instrument storage collection.</param>
		/// <param name="securityId">Identifier.</param>
		/// <returns>Instrument.</returns>
		public static Security ReadBySecurityId(this IStorageEntityList<Security> securities, SecurityId securityId)
		{
			if (securities == null)
				throw new ArgumentNullException(nameof(securities));

			if (securityId.IsDefault())
				throw new ArgumentNullException(nameof(securityId));

			return securities.ReadById(securityId.ToStringId());
		}

		internal static DateTimeOffset Truncate(this DateTimeOffset time)
		{
			return time.Truncate(TimeSpan.TicksPerMillisecond);
		}

		/// <summary>
		/// Synchronize securities with storage.
		/// </summary>
		/// <param name="drives">Storage drives.</param>
		/// <param name="securityStorage">Securities meta info storage.</param>
		/// <param name="exchangeInfoProvider">Exchanges and trading boards provider.</param>
		/// <param name="newSecurity">The handler through which a new instrument will be passed.</param>
		/// <param name="updateProgress">The handler through which a progress change will be passed.</param>
		/// <param name="addLog">The handler through which a new log message be passed.</param>
		/// <param name="isCancelled">The handler which returns an attribute of search cancel.</param>
		public static void SynchronizeSecurities(this IEnumerable<IMarketDataDrive> drives,
			ISecurityStorage securityStorage, IExchangeInfoProvider exchangeInfoProvider,
			Action<Security> newSecurity,
			Action<int, int> updateProgress, Action<LogMessage> addLog, Func<bool> isCancelled)
		{
			if (drives == null)
				throw new ArgumentNullException(nameof(drives));

			if (securityStorage == null)
				throw new ArgumentNullException(nameof(securityStorage));

			if (exchangeInfoProvider == null)
				throw new ArgumentNullException(nameof(exchangeInfoProvider));

			if (newSecurity == null)
				throw new ArgumentNullException(nameof(newSecurity));

			if (updateProgress == null)
				throw new ArgumentNullException(nameof(updateProgress));

			if (addLog == null)
				throw new ArgumentNullException(nameof(addLog));

			if (isCancelled == null)
				throw new ArgumentNullException(nameof(isCancelled));

			var securityPaths = new List<string>();
			var progress = 0;

			foreach (var dir in drives.Select(drive => drive.Path).Distinct())
			{
				foreach (var letterDir in InteropHelper.GetDirectories(dir))
				{
					if (isCancelled())
						break;

					var name = Path.GetFileName(letterDir);

					if (name == null || name.Length != 1)
						continue;

					securityPaths.AddRange(InteropHelper.GetDirectories(letterDir));
				}

				if (isCancelled())
					break;
			}

			if (isCancelled())
				return;

			// кол-во проходов по директории для создания инструмента
			var iterCount = securityPaths.Count;

			updateProgress(0, iterCount);

			var logSource = ConfigManager.GetService<LogManager>().Application;

			var securities = securityStorage.LookupAll().ToDictionary(s => s.Id, s => s, StringComparer.InvariantCultureIgnoreCase);

			foreach (var securityPath in securityPaths)
			{
				if (isCancelled())
					break;

				var securityId = Path.GetFileName(securityPath).FolderNameToSecurityId();

				var isNew = false;

				var security = securities.TryGetValue(securityId);

				if (security == null)
				{
					var firstDataFile =
						Directory.EnumerateDirectories(securityPath)
							.SelectMany(d => Directory.EnumerateFiles(d, "*.bin")
								.Concat(Directory.EnumerateFiles(d, "*.csv"))
								.OrderBy(f => Path.GetExtension(f).CompareIgnoreCase(".bin") ? 0 : 1))
							.FirstOrDefault();

					if (firstDataFile != null)
					{
						var id = securityId.ToSecurityId();

						decimal priceStep;

						if (Path.GetExtension(firstDataFile).CompareIgnoreCase(".bin"))
						{
							try
							{
								priceStep = File.ReadAllBytes(firstDataFile).Range(6, 16).To<decimal>();
							}
							catch (Exception ex)
							{
								throw new InvalidOperationException(LocalizedStrings.Str2929Params.Put(firstDataFile), ex);
							}
						}
						else
							priceStep = 0.01m;

						security = new Security
						{
							Id = securityId,
							PriceStep = priceStep,
							Name = id.SecurityCode,
							Code = id.SecurityCode,
							Board = exchangeInfoProvider.GetOrCreateBoard(id.BoardCode),
						};

						securities.Add(securityId, security);

						securityStorage.Save(security);
						newSecurity(security);

						isNew = true;
					}
				}

				updateProgress(progress++, iterCount);

				if (isNew)
					addLog(new LogMessage(logSource, TimeHelper.NowWithOffset, LogLevels.Info, LocalizedStrings.Str2930Params.Put(security)));
			}
		}

		/// <summary>
		/// Clear dates cache for storages.
		/// </summary>
		/// <param name="drives">Storage drives.</param>
		/// <param name="updateProgress">The handler through which a progress change will be passed.</param>
		/// <param name="addLog">The handler through which a new log message be passed.</param>
		/// <param name="isCancelled">The handler which returns an attribute of search cancel.</param>
		public static void ClearDatesCache(this IEnumerable<IMarketDataDrive> drives, Action<int, int> updateProgress, Action<LogMessage> addLog, Func<bool> isCancelled)
		{
			if (drives == null)
				throw new ArgumentNullException(nameof(drives));

			if (addLog == null)
				throw new ArgumentNullException(nameof(addLog));

			if (isCancelled == null)
				throw new ArgumentNullException(nameof(isCancelled));

			//var dataTypes = new[]
			//{
			//	Tuple.Create(typeof(ExecutionMessage), (object)ExecutionTypes.Tick),
			//	Tuple.Create(typeof(ExecutionMessage), (object)ExecutionTypes.OrderLog),
			//	Tuple.Create(typeof(ExecutionMessage), (object)ExecutionTypes.Order),
			//	Tuple.Create(typeof(ExecutionMessage), (object)ExecutionTypes.Trade),
			//	Tuple.Create(typeof(QuoteChangeMessage), (object)null),
			//	Tuple.Create(typeof(Level1ChangeMessage), (object)null),
			//	Tuple.Create(typeof(NewsMessage), (object)null)
			//};

			var logSource = ConfigManager.GetService<LogManager>().Application;
			var formats = Enumerator.GetValues<StorageFormats>().ToArray();
			var progress = 0;

			var marketDataDrives = drives as IMarketDataDrive[] ?? drives.ToArray();
			var iterCount = marketDataDrives.Sum(d => d.AvailableSecurities.Count()); // кол-во сбросов кэша дат

			updateProgress(progress, iterCount);

			foreach (var drive in marketDataDrives)
			{
				foreach (var secId in drive.AvailableSecurities)
				{
					foreach (var format in formats)
					{
						foreach (var dataType in drive.GetAvailableDataTypes(secId, format))
						{
							if (isCancelled())
								break;

							drive
								.GetStorageDrive(secId, dataType.MessageType, dataType.Arg, format)
								.ClearDatesCache();
						}
					}

					if (isCancelled())
						break;

					updateProgress(progress++, iterCount);

					addLog(new LogMessage(logSource, TimeHelper.NowWithOffset, LogLevels.Info, LocalizedStrings.Str2931Params.Put(secId, drive.Path)));
				}

				if (isCancelled())
					break;
			}
		}
	}
}