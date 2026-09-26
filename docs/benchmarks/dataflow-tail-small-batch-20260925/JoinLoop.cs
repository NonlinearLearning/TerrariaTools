public static class Sample { public static int Run(int seed, int limit, bool choose) { int total = seed;
for (int i0 = 0; (choose = i0 < limit); i0++) {
if (choose) { total = total + i0; } else { total = total - 0; }
if (choose) { total = total + i0; } else { total = total - 1; }
if (choose) { total = total + i0; } else { total = total - 2; }
if (choose) { total = total + i0; } else { total = total - 3; }
}
for (int i1 = 0; (choose = i1 < limit); i1++) {
if (choose) { total = total + i1; } else { total = total - 0; }
if (choose) { total = total + i1; } else { total = total - 1; }
if (choose) { total = total + i1; } else { total = total - 2; }
if (choose) { total = total + i1; } else { total = total - 3; }
}
for (int i2 = 0; (choose = i2 < limit); i2++) {
if (choose) { total = total + i2; } else { total = total - 0; }
if (choose) { total = total + i2; } else { total = total - 1; }
if (choose) { total = total + i2; } else { total = total - 2; }
if (choose) { total = total + i2; } else { total = total - 3; }
}
for (int i3 = 0; (choose = i3 < limit); i3++) {
if (choose) { total = total + i3; } else { total = total - 0; }
if (choose) { total = total + i3; } else { total = total - 1; }
if (choose) { total = total + i3; } else { total = total - 2; }
if (choose) { total = total + i3; } else { total = total - 3; }
}
return total; } }
