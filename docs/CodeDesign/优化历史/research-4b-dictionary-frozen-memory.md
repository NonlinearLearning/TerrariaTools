# 4b. Microsoft first-party notes on Dictionary/FrozenDictionary memory

## Method / reachability notes (read first)

- **`web_search` is broken**: confirmed `HTTP 401 — Authentication Fails`. Abandoned immediately, never retried.
- **`web_fetch` truncates all three Toub posts** before the Collections section (it returned only the article header, TOC and "Benchmarking Setup", then `(Content truncated.)`). This includes fragment URLs — `#dictionary` did **not** change the returned text.
- **Therefore I bypassed `web_fetch` for the posts**: downloaded the complete raw HTML with `Invoke-WebRequest` (all HTTP 200, 1.15 MB / 964 KB / 785 KB) and stripped tags to plain text locally. **Nothing was truncated.** Every section you asked about was reached and read in full:
  - .NET 8 `Collections → Dictionary` (body line 10121) and `Collections → Frozen Collections` (body line 10491) — both confirmed present as TOC entries *and* body sections.
  - .NET 9 `Collections → Core Collections` (body line 8630).
  - .NET 10 `Collections` (body line 4375), incl. `Frozen Collections` (5328) and `Other Collections` (5520).
- **No "COULD NOT REACH due to page truncation" cases remain** for the three posts.
- **404**: `https://devblogs.microsoft.com/dotnet/announcing-net-8/` returns **404 (Not Found)** — COULD NOT REACH. The working URL is `https://devblogs.microsoft.com/dotnet/announcing-dotnet-8/` (see item 6).

---

## 1. Dictionary memory footprint / Entry layout / load factor / capacity / EnsureCapacity / TrimExcess

- **COULD NOT VERIFY a direct statement about `Dictionary<TKey,TValue>`'s `Entry` struct layout, its load factor, or its per-entry memory footprint.** The strings `load factor`, `EnsureCapacity`, `Entry[]`, and `ReferenceEqualityComparer` occur **0 times** across all three posts. Toub never discusses Dictionary's internal `Entry`/`entries`/`buckets` sizing arithmetic in these three posts.

- **`HashSet<T>.TrimExcess(int capacity)` was added in .NET 9** (this is the only `TrimExcess` mention of the three posts, and it is HashSet/Queue/Stack, **not** Dictionary):
  > "dotnet/runtime#85877 from @hrrrrustic added a TrimExcess(int capacity) method to HashSet<T> (as well as to Queue<T> and Stack<T>), enabling more fine-grained control over how much memory to cull from a set that might have grown larger than is now required."
  — https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-9/

- **Memory argument for HashSet over `Dictionary<T,T>`** — .NET 9 states this most explicitly:
  > "dotnet/runtime#96573 from @ndsvw also identified a few places in various libraries where a Dictionary<T, T> was being used as a set and replaced them with HashSet<T>. The implementations of Dictionary<> and HashSet<> are very close in nature, but the latter consumes less memory because it doesn't need to store separate values. Using a Dictionary<T, T> effectively doubles the required storage, so if a HashSet<T> suffices, it's preferable."
  — https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-9/

- The same idea appears earlier in .NET 8 (Peanut Butter section):
  > "dotnet/runtime#89030 fixed a case where a Dictionary<T, T> was being used as a set. Changing it to instead be HashSet<T> saves on the internal storage for the values that end up being identical to the keys."
  — https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-8/

- **Capacity / presizing (allocation reduction), .NET 8** — the closest thing to a "capacity" claim:
  > "dotnet/runtime#75850 removed some allocations as part of initializing a Dictionary<,>. The dictionary in TypeConverter gets populated with a fixed set of predetermined items, and as such it's provided with a capacity so as to presize its internal arrays to avoid intermediate allocations as part of growing. However, the provided capacity was smaller than the number of items actually being added. This PR simply fixed the number, and voila, less allocation."
  — https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-8/

- **Capacity + span sharing in `ToDictionary`, .NET 9**:
  > "dotnet/runtime#96574 from @xin9le. The PR changes the code to do a better job setting the capacity of the Dictionary<TKey, TValue> prior to filling it, and also using the CollectionsMarshal.AsSpan to share code for handling sources that are arrays and lists, while also shaving off some overhead by enumerating the span instead of the list directly."
  Measured: `EnumerableToDictionary` .NET 8 = 284.3 us / 788.73 KB → .NET 9 = 149.9 us / 237.01 KB (Ratio 0.53, **Alloc Ratio 0.30**).
  — https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-9/

- **Dictionary constructor special-casing `KeyValuePair<TKey,TValue>[]` / `List<...>`, .NET 8**:
  > "With dotnet/runtime#86254, it now also special-cases when the enumerable is a KeyValuePair<TKey, TValue>[] or a List<KeyValuePair<TKey, TValue>>. When such a source is found, a span is extracted from it (a simple cast for an array, or via CollectionsMarshal.AsSpan for a List<>), and then that span (rather than the original IEnumerable<>) is what's enumerated. That saves an enumerator allocation and several interface dispatches per item for these reasonably common cases."
  Measured: `FromList` .NET 7 = 12.250 us → .NET 8 = 6.780 us (Ratio 0.55).
  — https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-8/

- **`ConcurrentDictionary` re-sizing on `Clear` (memory allocation), .NET 10**:
  > "dotnet/runtime#108065 from @koenigst changes how a ConcurrentDictionary's backing array is sized when it's cleared. ... Due to the concurrent nature of the dictionary and its implementation, Clear'ing it necessitates creating a new array rather than just using part of the old one. When that new array was created, it reset to using the default size. This PR tweaks that to remember the initial capacity requested by the user, and using that initial size again when constructing the new array."
  Measured: `ClearAndAdd` .NET 9 = 51.95 us / 134.36 KB → .NET 10 = 30.32 us / 48.73 KB (Ratio 0.58, **Alloc Ratio 0.36**).
  — https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-10/

- **Double-lookup elimination via analyzer guidance, .NET 8**:
  > "This incurs two lookups, one as part of ContainsKey, and then if the key wasn't in the dictionary, another as part of the Add call. Code can instead achieve the same operation with: `dictionary.TryAdd(key, value);` which incurs only one lookup. CA1864, added in dotnet/roslyn-analyzers#6199 from @CollinAlpert, looks for such places where an Add call is guarded by a ContainsKey call."
  Measured: `ContainsThenAdd` 25.93 ns → `TryAdd` 19.50 ns (Ratio 0.75). HashSet analogue: 22.98 ns → 17.99 ns (0.78).
  — https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-8/

---

## 2. Introduction of FrozenDictionary/FrozenSet in .NET 8 — problem solved + tradeoffs

- **The introduction claim**:
  > "However, enough of a need has presented itself that .NET 8 sees the introduction of not one but two new collection types: System.Collections.Frozen.FrozenDictionary<TKey, TValue> and System.Collections.Frozen.FrozenSet<TKey, TValue>."
  *(Note: the source post writes `FrozenSet<TKey, TValue>` here; the real type is `FrozenSet<T>` with one type parameter. Quoted exactly as published, not corrected.)*
  — https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-8/

- **What problem it solves — read-optimized immutability, explicit willingness to pay construction cost**:
  > "And that's where frozen collections come in. The collections in System.Collections.Frozen are immutable, just as are those in System.Collections.Immutable, but they're optimized for a different scenario. Whereas the purpose of a type like ImmutableDictionary<TKey, TValue> is to enable efficient mutation (into a new instance), the purpose of FrozenDictionary<TKey, TValue> is to represent data that never changes, and thus it doesn't expose any operations that suggest mutation, only operations for reading. ... Whatever the scenario, you're creating an immutable collection that you want to be optimized for reads, and you're willing to spend some more cycles creating the collection (because you do it only once, or only once in a while) in order to make reads as fast as possible. That's exactly what FrozenDictionary<TKey, TValue> and FrozenSet<T> provide."
  — https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-8/

- **Why ImmutableDictionary was rejected as the read-optimized answer (O(log n) lookups)**:
  > "However, just because the above numbers look amazing doesn't mean ImmutableDictionary<TKey, TValue> is always the right tool for the immutable job… it actually rarely is. Why? Because the exact thing that made it so fast and memory efficient for the above benchmark is also its downfall on one of the most common tasks needed for an 'immutable' dictionary: reading. With its tree-based data structure, not only are adds O(log n), but lookups are also O(log n), which for a large dictionary can be extremely inefficient when compared to the O(1) access times of a type like Dictionary<TKey, TValue>."
  — https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-8/

- **Construction cost — acknowledged as an initial non-goal, and the "minutes" pit of failure**:
  > "And as such, a lot of attention was paid to overheads involved in reading from the collection, but initially very little time was paid to optimizing construction time. In fact, improving construction time was initially a non-goal, with a willingness to spend as much time as was needed to eke out more throughput for reading."
  > "As a stop-gap measure, dotnet/runtime#81194 changed the existing ToFrozenDictionary/ToFrozenSet methods to not do any analysis of the incoming data, and instead have both construction time and read throughput in line with that of Dictionary/HashSet. It then added new overloads with a bool optimizeForReading argument, to enable developers to opt-in to those longer construction times in exchange for better read throughput. ... but it also helped developers avoid pits of failure by using what looked like a harmless method but could result in significant increases in processing time (one degenerate example I created resulted in ToFrozenDictionary running literally for minutes)."
  — https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-8/

- **Construction cost was fixed, and the `optimizeForReading` overloads were removed again before shipping .NET 8**:
  > "With all of those optimizations in place, construction time has now improved to the point where it's no longer a threat, and dotnet/runtime#87988 effectively reverted dotnet/runtime#81194, getting rid of the optimizeForReading-based overloads, such that everything is now optimized for reading."
  — https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-8/

- **Construction-time memory/allocation tradeoffs (component PRs)**:
  > "dotnet/runtime#81603 moved some code around to reduce how much code was in a generic context. With Native AOT, with type parameters involving value types, every unique set of type parameters used with these collections results in a unique copy of the code being made ... This change was able to shave ~10Kb off each generic instantiation." *(code size, not GC heap)*
  > "dotnet/runtime#87876 and dotnet/runtime#87989 improve the 'LengthBucket' strategy ... The initial implementation used an array of arrays, and this PR flattens that into a single array. This makes construction time much faster for this strategy, as there's significantly less allocation involved."
  — https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-8/

- **Measured lookup numbers (.NET 8), 10,000 `int` keys**:
  > "Whereas for this lookup test Dictionary<TKey, TValue> was ~9x faster than ImmutableDictionary<TKey, TValue>, FrozenDictionary<TKey, TValue> was 50% faster than even Dictionary<TKey, TValue>."
  Table: `ImmutableDictionaryGets` 360.55 us (Ratio 13.89) / `DictionaryGets` 39.43 us (1.52) / `FrozenDictionaryGets` 25.95 us (1.00, baseline).
  — https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-8/

- **Measured FrozenSet vs HashSet (.NET 8)**: `HashSet_IsMostPopular` 9.824 ns (1.00) → `FrozenSet_IsMostPopular` 1.518 ns (**Ratio 0.15**). Same section notes the chosen impl was `System.Collections.Frozen.LengthBucketsFrozenSet`.
  — https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-8/

- **Measured ImmutableDictionary vs Dictionary enumeration/indexer (.NET 8), 1,000,000 items**:
  > "Uh oh. Our ImmutableDictionary<TKey, TValue> in this example is ~12x as expensive for lookups and ~20x as expensive for enumeration as Dictionary<TKey, TValue>."
  Table: `IndexerImmutableDictionary` 46.538 ms vs `IndexerDictionary` 3.780 ms; `EnumerateImmutableDictionary` 28.065 ms vs `EnumerateDictionary` 1.404 ms.
  — https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-8/

- **ImmutableDictionary vs "Dictionary treated as immutable" for adds (memory + throughput), .NET 8**:
  > "That highlights that the tree-based nature of ImmutableDictionary<TKey, TValue> makes it significantly more efficient (~120x better in both throughput and allocation in this run) for this example of performing lots of additions ..."
  Table: `DictionaryAdds` 478.961 ms (1.000) → `ImmutableDictionaryAdds` 4.067 ms (0.009).
  — https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-8/

- **.NET 10 restatement of the tradeoff (construction time vs read speed)**:
  > "The FrozenDictionary<TKey, TValue> and FrozenSet<T> collection types were introduced in .NET 8 as collections optimized for the common scenario of creating a long-lived collection that's then read from a lot. They spend more time at construction in exchange for faster read operations. Under the covers, this is achieved in part by having specializations of the implementations that are optimized for different types of data or shapes of input."
  — https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-10/

- **.NET 10: explicit "not too much additional space" claim for the dense-integer specialization**:
  > "In particular, they handle the common case where these values are densely packed, in which case they implement the dictionary as an array that it can index into based on the integer's value. This makes for a very efficient lookup, while not consuming too much additional space: it's only used when the values are dense and thus won't be wasting many empty slots in the array."
  Measured: `Get` .NET 9 = 2.0660 ns → .NET 10 = 0.8735 ns (**Ratio 0.42**).
  — https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-10/

- **.NET 10: FrozenCollections alternate-lookup GVM fix**:
  > "dotnet/runtime#108732 from @andrewjsaid addresses this by changing the frequency with which a GVM needs to be invoked. Rather than the lookup operation itself being a generic virtual method, the PR introduces a separate generic virtual method that retrieves a delegate for performing the lookup; the retrieval of that delegate still incurs GVM penalties, but once the delegate is retrieved, it can be cached, and invoking it does not incur said overheads."
  Measured: `Get` .NET 9 = 133.46 ns → .NET 10 = 81.39 ns (**Ratio 0.61**).
  — https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-10/

---

## 3. `CollectionsMarshal.GetValueRefOrAddDefault` / `AsSpan` / `SetCount`

- **`CollectionsMarshal.GetValueRefOrAddDefault` and double-lookup avoidance** — described in the **.NET 9** post (it was introduced in .NET 6; the .NET 8 post does **not** mention it):
  > "For fun, we can also take this example one step further. .NET 6 introduced the CollectionsMarshal.GetValueRefOrAddDefault method, which returns a writable ref to the actual location where the TValue for a given TKey is stored, creating the entry in the dictionary if it doesn't exist. This is very handy for operations like the one used above, as it helps to avoid an extra dictionary lookup. Without it, we're doing one lookup as part of the TryGetValue and then another lookup as part of the setter, but with it, we just do the single lookup as part of GetValueRefOrAddDefault and then no additional lookup is necessary because we already have the location into which we can directly write. And as the lookups in this benchmark are one of the more costly elements, eliminating half of them can significantly reduce the cost of the operation. As part of this alternate key effort, a new overload of GetValueRefOrAddDefault was added that works with it, such that the same operation can be performed with a TAlternateKey."
  Measured (word-count benchmark): `CountWords1` 60.73 ms / 20.67 MB (1.00) → `CountWords2` 54.01 ms / 2.54 MB (0.89 / 0.12) → `CountWords3` (using `CollectionsMarshal.GetValueRefOrAddDefault(alternate, word, out _)++`) **44.38 ms / 2.54 MB (Ratio 0.73, Alloc Ratio 0.12)**.
  — https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-9/

- **`CollectionsMarshal.SetCount(List<T>, int)` was added in .NET 8** and is what enables writing into a list's backing store beyond its current `Count`:
  > "In .NET 5, the CollectionsMarshal.AsSpan(List<T>) method was added; it returns a Span<T> for the in-use area of a List<T>'s backing store. ... This is very useful for a variety of scenarios, in particular for consuming a List<T>'s data via span-based APIs. It doesn't, however, enable scenarios that want to efficiently write to a List<T>, in particular where it would require increasing a List<T>'s count."
  > "This PR adds the new SetCount method, which does just that."
  Usage shown: `CollectionsMarshal.SetCount(list, 100); Span<char> span = CollectionsMarshal.AsSpan(list); span.Fill('a');`
  > "That new SetCount method is not only exposed publicly, it's also used as an implementation detail now in LINQ ... thanks to dotnet/runtime#85288."
  — https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-8/

- **`CollectionsMarshal.AsSpan` reuse in `ToList` (.NET 9)**:
  > "dotnet/runtime#104365 from @andrewjsaid followed-up on this to use that same SegmentedArrayBuilder to improve ToList. ... rather than allocating an array, it allocates a List<T> and uses the CollectionsMarshal.SetCount method to set both the Capacity and Count of the list to the desired size, then copies the elements directly into the backing array for the list, thanks to CollectionsMarshal.AsSpan."
  — https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-9/

- **`CollectionsMarshal.AsBytes(BitArray)` was added in .NET 10**:
  > "dotnet/runtime#116308 adds a CollectionsMarshal.AsBytes(BitArray) method that returns a Span<byte> directly referencing the BitArray's underlying storage. This provides a very efficient way to get access to all the bits, which then makes it possible to write (or reuse) vectorized algorithms."
  — https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-10/

- Occurrence counts across the full posts: `CollectionsMarshal` = 9 (.NET 8) / 8 (.NET 9) / 5 (.NET 10). `GetValueRefOrAddDefault` = 0 / 4 / 0.

---

## 4. Dictionary performance work in .NET 9 / .NET 10

- **`GetAlternateLookup` / `IAlternateEqualityComparer<TAlternate,T>` introduced in .NET 9**:
  > "And in .NET 9, it gets a performance-focused feature I've been wanting for years."
  > "This has been addressed in .NET 9 with the introduction of IAlternateEqualityComparer<TAlternate, T>. A comparer that implements IEqualityComparer<T> may now also implement this additional interface one or more times for other TAlternate types, making it possible for that comparer to treat alternate types the same as the T. Then a type like Dictionary<TKey, TValue> can expose additional methods that work in terms of a TAlternateKey and allow them to work if the comparer in that Dictionary<TKey, TValue> implements IAlternateEqualityComparer<TAlternateKey, TKey>. In .NET 9 with dotnet/runtime#102907 and dotnet/runtime#103191, Dictionary<TKey, TValue>, ConcurrentDictionary<TKey, TValue>, FrozenDictionary<TKey, TValue>, HashSet<T>, and FrozenSet<T> all do exactly that."
  — https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-9/

- **Why it matters (allocation) and the measured result**:
  > "I'm returning a Dictionary<string, int>, so I certainly need to materialize the string for each ReadOnlySpan<char> in order to store it in the dictionary, but I should only need to do so once, the first time the word is found."
  > "Note the distinct lack of a ToString(), which means no allocation will occur here for words already seen."
  > "Note the huge reduction in allocation."
  Measured: `CountWords1` 60.35 ms / 20.67 MB (1.00) → `CountWords2` 57.40 ms / **2.54 MB (Ratio 0.95, Alloc Ratio 0.12)**.
  — https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-9/

- **Which comparers support it**:
  > "But with string and ReadOnlySpan<char> being so common, it'd be a shame if there wasn't built-in support for this combination. And indeed, with the aforementioned PRs, all of the built-in StringComparer types implement IAlternateEqualityComparer<ReadOnlySpan<char>, string>."
  > "'But wait, there's more!' dotnet/runtime#104202 extends the alternate comparer implementation for string/ReadOnlySpan<char> further to also apply to EqualityComparer<string>.Default, which means that if you don't supply a comparer at all, these collection types will still support ReadOnlySpan<char> lookups."
  Side effect measured: `Count` .NET 8 = 4.477 us → .NET 9 = 2.808 us (**Ratio 0.63**), attributed to `StringEqualityComparer` being non-generic.
  — https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-9/

- **`TryGetAlternateLookup` exists alongside `GetAlternateLookup`**:
  > "Now with .NET 9, a new GetAlternateLookup method (and a corresponding TryGetAlternateLookup) exists to produce a separate value type wrapper that enables using an alternate key type for all the relevant operations"
  — https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-9/

- **.NET 10 `Dictionary<TKey,TValue>` work — constant-string lookup fast path**:
  > "dotnet/runtime#117427 makes dictionary lookups with constant strings much faster. You might expect it would be a complicated change, but it ends up being just a few strategic tweaks. A variety of methods for operating on strings are already known to the JIT and already have optimized implementations for when dealing with constants. All this PR needed to do was change which methods Dictionary<TKey, TValue> was using in its optimized TryGetValue lookup path, and because that path is often inlined, a constant argument to TryGetValue can be exposed as a constant to these helpers, e.g. string.Equals."
  Measured: `Get` .NET 9 = 33.81 ns → .NET 10 = 14.02 ns (**Ratio 0.41**).
  — https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-10/

- **.NET 10 `OrderedDictionary<TKey,TValue>` — index-returning overloads (another double-lookup elimination)**:
  > "dotnet/runtime#109324 adds new overloads of TryAdd and TryGetValue that provide the index of the added or retrieved element in the collection. This index can then be used in subsequent operations on the dictionary to access the same slot. For example, if you want to implement an AddOrUpdate operation on top of OrderedDictionary, you need to perform one or two operations, first trying to add the item, and then if found to already exist, updating it, and that update can benefit from targeting the exact index that contains the element rather than it needing to do another keyed lookup."
  — https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-10/

- **`Dictionary.Remove(key, out value)` overload: COULD NOT VERIFY.** No such overload is mentioned in any of the three posts (searched for `Remove(` with an `out` parameter across all three — only hit is `ConcurrentDictionary.TryRemove`). It is not covered by Toub in these posts.
- **`ReferenceEqualityComparer`: COULD NOT VERIFY / NOT MENTIONED.** 0 occurrences across all three posts.

---

## 5. FrozenDictionary "high cost to create" — exact wording confirmed

- **Confirmed verbatim** from the Remarks section:
  > "FrozenDictionary<TKey,TValue> is immutable and is optimized for situations where a dictionary is created infrequently but is used frequently at run time. It has a relatively high cost to create but provides excellent lookup performance. Thus, it is ideal for cases where a dictionary is created once, potentially at the startup of an application, and is used throughout the remainder of the life of the application. FrozenDictionary<TKey,TValue> should only be initialized with trusted keys, as the details of the keys impacts construction time."
  — https://learn.microsoft.com/en-us/dotnet/api/system.collections.frozen.frozendictionary-2
  *(the URL redirected to `?view=net-11.0-pp`; assembly listed as System.Collections.Immutable.dll)*

  ⚠️ **Scope caveat**: this Remarks text makes **no claim about memory footprint**. It says nothing about FrozenDictionary being larger or smaller than Dictionary in memory. The only memory-related statement in that Remarks block is the *trusted keys* note, which is about construction **time**. Do not paraphrase this into a memory claim.

---

## 6. .NET 8 announcement / release notes

- **`https://devblogs.microsoft.com/dotnet/announcing-net-8/` → HTTP 404 (Not Found).** COULD NOT REACH. Same 404 for `announcing-net-8-0/` and `learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-8/libraries`.
- **Working announcement URL: `https://devblogs.microsoft.com/dotnet/announcing-dotnet-8/`** (HTTP 200, 279 KB). **It contains no FrozenDictionary/FrozenSet content at all** — zero matches for `Frozen`, `Dictionary`, or `CollectionsMarshal`. Its only collection-adjacent content is the C# 12 collection-expressions feature. So this page is **not** a source for the FrozenCollections claim.
- **The .NET 8 release-notes page that DOES cover FrozenCollections is the runtime "what's new" page** — https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-8/runtime:
  > "The new System.Collections.Frozen namespace includes the collection types FrozenDictionary<TKey,TValue> and FrozenSet<T>. These types don't allow any changes to keys and values once a collection is created. That requirement allows faster read operations (for example, TryGetValue()). These types are particularly useful for collections that are populated on first use and then persisted for the duration of a long-lived service, for example:"
  > `private static readonly FrozenDictionary<string, bool> s_configurationData = LoadConfigurationData().ToFrozenDictionary();`
  — This page likewise makes **no memory-footprint claim** for the frozen collections.

---

## Bottom line for the parent

- **Reachable and fully quoted**: .NET 8 Collections→Dictionary + Frozen Collections; .NET 9 Collections→Core Collections; .NET 10 Collections incl. Frozen Collections; MS Learn FrozenDictionary Remarks.
- **404**: `announcing-net-8/` — use `announcing-dotnet-8/` (but it has no FrozenColumn content) or the `whats-new/dotnet-8/runtime` page (which does).
- **Genuinely absent from all three posts** (not truncation — verified by full-text search of the complete downloaded HTML): Dictionary `Entry` layout, load factor, `EnsureCapacity`, `Dictionary.TrimExcess`, `Dictionary.Remove(key, out value)`, `ReferenceEqualityComparer`.
- **The only first-party memory-footprint statements about collections found** are: `Dictionary<T,T>` vs `HashSet<T>` "effectively doubles the required storage" (.NET 9) and `ConcurrentDictionary.Clear` allocation 134.36 KB → 48.73 KB (.NET 10), plus the `ToDictionary` allocation 788.73 KB → 237.01 KB (.NET 9).
