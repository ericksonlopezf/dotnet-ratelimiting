# PROPERTY-BASED TEST SUITE GUIDE

Mathematical properties verified across random permutations:
1. **Property 1:** Never grant more permits than total capacity in any interval:
   $$\sum \text{granted} \le \text{Capacity}$$
2. **Property 2:** Rejections do not mutate counter state.
3. **Property 3:** Token refill never exceeds capacity:
   $$\text{Tokens}(t) \le \text{Capacity}$$
4. **Property 4:** Counter never decrements below zero.
5. **Property 5:** Determinism under identical inputs with FakeTimeProvider.
