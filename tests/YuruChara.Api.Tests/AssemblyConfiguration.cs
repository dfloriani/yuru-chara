using YuruChara.Api.Tests.TestSupport;

// One PostGIS container and one API host for the whole assembly, injected into any
// test class that declares a PostGisApiFixture constructor parameter. The
// alternative — a class fixture — would start and seed a container per test class,
// which is several seconds each for tests that only read.
[assembly: AssemblyFixture(typeof(PostGisApiFixture))]

// Test classes run one at a time rather than in parallel.
//
// This is not about the database: every test here is a read. It is about the output
// cache on /api/prefectures, which is process-wide state shared by every test in the
// assembly. OutputCacheTests needs to observe a cold cache and then a warm one, and
// it cannot do that while another class is issuing requests that warm it. With a
// suite this small, serialising it costs a second or two and removes the whole
// category of flake.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
