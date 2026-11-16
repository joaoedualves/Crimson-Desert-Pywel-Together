#include "mod/session2/clock_gate.h"

#include <windows.h>

#include <safetyhook.hpp>

#include <atomic>
#include <cstring>
#include <format>

#include "mod/game/safe_memory.h"
#include "mod/log.h"
#include "mod/session2/clock_push.h"
#include "mod/session2/server.h"

namespace cdmp::session2 {
namespace {

// Entries and one instruction of each body, from the copy analysed in
// docs/research/pendientes/reloj/capstone_*.txt (prologues alone repeat).
// Build 3663: FieldMover::SyncFieldTimeOnMove no longer exists as a function; the
// compiler inlined it into its only caller (old 0x2BBFA80 -> 0x2ECA480). The gate
// is a mid hook at the first instruction of the inlined body (mov rsi,[rbp-0x68]
// = the destination field; r15 = FM) and, for user 2, skips the body by jumping
// to kTravelCont (where the caller carries on after the inlined call).
constexpr uintptr_t kTravel = 0x2ECAA97;
constexpr uint8_t kTravelHead[] = {0x48, 0x8B, 0x75, 0x98, 0x48, 0x89, 0xB5, 0x30, 0x01, 0x00, 0x00};
constexpr uintptr_t kTravelOwnerCmp = 0x2ECAABD;  // cmp [rsi+0x180], eax
constexpr uint8_t kTravelOwnerCmpBytes[] = {0x39, 0x86, 0x80, 0x01, 0x00, 0x00};
constexpr uintptr_t kTravelGetterCall = 0x2ECABAF;  // call 0x231A180 (non-owner branch)
constexpr uint8_t kTravelGetterCallBytes[] = {0xE8, 0xCC, 0xF5, 0x44, 0xFF};
constexpr uintptr_t kTravelCont = 0x2ECABBF;  // mov rdx,rsi ; mov rcx,r15 ; call 0x2ECB660
constexpr uint8_t kTravelContBytes[] = {0x48, 0x8B, 0xD6, 0x49, 0x8B, 0xCF};
constexpr uintptr_t kLoading = 0x23DC630;
constexpr uint8_t kLoadingHead[] = {0x48, 0x89, 0x5C, 0x24, 0x10, 0x48, 0x89, 0x74, 0x24, 0x18, 0x48,
                                    0x89, 0x7C, 0x24, 0x20, 0x55, 0x41, 0x54, 0x41, 0x55, 0x41, 0x56,
                                    0x41, 0x57, 0x48, 0x8B, 0xEC, 0x48, 0x83, 0xEC, 0x60, 0x4D, 0x8B,
                                    0xE9, 0x49, 0x8B, 0xF0, 0x4C, 0x8B, 0xF2, 0x48, 0x8B, 0xD9};
constexpr uintptr_t kLoadingFlagCmp = 0x23DC66B;  // cmp [rbp+0x50], r12b ; je 0x219D941
constexpr uint8_t kLoadingFlagCmpBytes[] = {0x44, 0x38, 0x65, 0x50, 0x0F, 0x84, 0x0C, 0x01, 0x00, 0x00};
constexpr uintptr_t kGetter = 0x231A180;
constexpr uint8_t kGetterHead[] = {0x48, 0x89, 0x5C, 0x24, 0x10, 0x57, 0x48, 0x83, 0xEC, 0x60, 0x48, 0x8B, 0x01, 0x48,
                                   0x8B, 0xFA, 0x48, 0x8D, 0x54, 0x24, 0x70, 0x48, 0x8B, 0xD9, 0xFF, 0x50, 0x60};
constexpr uint64_t kMaxLogged = 24;

ClockGame g_game{};
uintptr_t g_base = 0;
SafetyHookInline g_loading;
SafetyHookMid g_travel;
std::atomic<bool> g_active{false};
std::atomic<uint64_t> g_travels{0}, g_travel_failed{0}, g_loadings{0}, g_flags_cut{0}, g_world_moved{0},
    g_logged{0}, g_world_changes{0}, g_world_frozen{0};
std::atomic<uint32_t> g_host_key{0};  // last host user key read (fallback when the lookup fails)
constexpr uint64_t kMaxWatchLogged = 60;

template <class T>
bool peek(uintptr_t a, T& v) {
    return a && game::safe_read(a, &v, sizeof v);
}

template <class... Args>
void note(std::format_string<Args...> fmt, Args&&... args) {
    if (g_logged.fetch_add(1) >= kMaxLogged) return;
    log::info(fmt, std::forward<Args>(args)...);
}

// The world clock, as the probe prints it (day, hour, minute, second, scale).
struct WorldTime {
    uint8_t raw[clk::kFieldTimeSize] = {};
    bool ok = false;
};

WorldTime world_now() {
    WorldTime w;
    w.ok = g_base && game::safe_read(g_base + clk::kWorldTime, w.raw, sizeof w.raw);
    return w;
}

std::string show(const uint8_t* ft) {
    uint32_t v[5] = {};
    uint16_t scale = 0;
    std::memcpy(v, ft, sizeof v);
    std::memcpy(&scale, ft + 0x16, sizeof scale);
    return std::format("day {} {:02}:{:02}:{:02} scale {}", v[0], v[1], v[2], v[3], scale);
}

std::string show(const WorldTime& w) { return w.ok ? show(w.raw) : std::string("unreadable"); }

// The global holds a base and its anchor: any change of its 32 bytes is a
// publish. Only two good reads count (no false "moved").
bool moved(const WorldTime& a, const WorldTime& b) {
    return a.ok && b.ok && std::memcmp(a.raw, b.raw, sizeof a.raw) != 0;
}

uint16_t scale_of(const WorldTime& w) {
    uint16_t s = 0;
    std::memcpy(&s, w.raw + 0x16, sizeof s);
    return s;
}

ClockWho who_now(uintptr_t ts) {
    uintptr_t host = 0, user2 = 0;
    uint32_t key = 0;
    if (server_host_user(host) && peek(host + clk::kUserKey, key) && key) {
        g_host_key.store(key);
    } else {
        key = g_host_key.load();
    }
    if (!server_user2(user2)) user2 = 0;
    return clock_who(ts, key, user2);
}

// Once a second: every publish of the world clock, by whoever.
DWORD WINAPI watch_thread(LPVOID) {
    WorldTime last = world_now();
    for (;;) {
        Sleep(1000);
        // P8 session 25 (clock_push.h, T2): the world clock to user 2 when it changed while user 2 is in the world.
        clock_push_tick(GetTickCount64());
        const WorldTime now = world_now();
        if (!moved(last, now)) {
            if (now.ok) last = now;
            continue;
        }
        g_world_changes.fetch_add(1);
        const bool frozen = scale_of(now) == 0;
        if (frozen) g_world_frozen.fetch_add(1);
        if (g_world_changes.load() <= kMaxWatchLogged || frozen) {
            log::info("Session2 clock watch: world clock published: {} -> {}{}", show(last), show(now),
                      frozen ? " | FROZEN (scale 0)" : "");
        }
        last = now;
    }
}

uintptr_t ts_of(void* fm) {
    uintptr_t ts = 0;
    return peek(reinterpret_cast<uintptr_t>(fm), ts) ? ts : 0;
}

// The inlined FieldMover::SyncFieldTimeOnMove (build 3663): r15 = FM, [rbp-0x68] =
// destination field. For user 2 the body never runs: its character takes the
// field's time and the caller carries on at kTravelCont (as the old detour, which
// returned without calling the original).
void travel_mid(safetyhook::Context& ctx) noexcept {
    try {
        void* fm = reinterpret_cast<void*>(ctx.r15);
        uintptr_t field = 0;
        if (!peek(ctx.rbp - 0x68, field) || !field) return;
        const uintptr_t ts = ts_of(fm);
        if (!ts || who_now(ts) != ClockWho::User2) return;
        g_travels.fetch_add(1);
        const WorldTime before = world_now();
        const bool copied = clock_user2_travel(g_game, ts, field);
        const WorldTime after = world_now();
        uint8_t mine[clk::kFieldTimeSize] = {};
        game::safe_read(ts + clk::kTsFieldTime, mine, sizeof mine);
        if (!copied) g_travel_failed.fetch_add(1);
        if (moved(before, after)) g_world_moved.fetch_add(1);
        uint32_t owner = 0;
        peek(field + 0x180, owner);
        note("Session2 clock gate: user 2 travels to field 0x{:X} (owner 0x{:X}): {} | world clock {} -> {}{}", field,
             owner,
             copied ? "its character takes the field's time (" + show(mine) + "), nothing published"
                    : std::string("COPY FAILED, its character keeps its time; nothing published"),
             show(before), show(after), moved(before, after) ? " (MOVED, by someone else)" : "");
        // What the skipped instructions and body leave for the caller: rsi = field, [rbp+0x130] = field.
        ctx.rsi = field;
        *reinterpret_cast<uintptr_t*>(ctx.rbp + 0x130) = field;
        ctx.rip = g_base + kTravelCont;
    } catch (...) {
    }
}

// TransformSync LoadingComplete(TS, res, field, ?, u8 flag, now*) -> res.
uint32_t* __fastcall loading_detour(void* ts, uint32_t* res, void* field, void* r9, uint8_t flag,
                                    uint64_t* now) noexcept {
    uint8_t eff = flag;
    bool mine = false;
    WorldTime before;
    try {
        if (who_now(reinterpret_cast<uintptr_t>(ts)) == ClockWho::User2) {
            mine = true;
            eff = clock_loading_flag(ClockWho::User2, flag);
            g_loadings.fetch_add(1);
            if (eff != flag) g_flags_cut.fetch_add(1);
            before = world_now();
        }
    } catch (...) {
        if (mine) eff = 0;
    }
    uint32_t* r = g_loading.unsafe_call<uint32_t*>(ts, res, field, r9, eff, now);
    if (mine) {
        try {
            const WorldTime after = world_now();
            if (moved(before, after)) g_world_moved.fetch_add(1);
            uint32_t code = 0;
            peek(reinterpret_cast<uintptr_t>(r), code);
            note("Session2 clock gate: user 2's loading completes in field 0x{:X} (flag {} -> {}, result 0x{:X}) | "
                 "world clock {} -> {}{}",
                 reinterpret_cast<uintptr_t>(field), flag, eff, code, show(before), show(after),
                 moved(before, after) ? " (MOVED)" : "");
        } catch (...) {
        }
    }
    return r;
}

bool code_ok(uintptr_t rva, const uint8_t* bytes, size_t n, const char* what) {
    uint8_t actual[64] = {};
    if (n <= sizeof actual && game::safe_read(g_base + rva, actual, n) && std::memcmp(actual, bytes, n) == 0) {
        return true;
    }
    log::error("Session2 clock gate: {} (exe+0x{:X}) is not the expected code; the gate stays off", what, rva);
    return false;
}

}  // namespace

ClockWho clock_who(uintptr_t ts, uint32_t host_key, uintptr_t user2) {
    uintptr_t actor = 0, user = 0;
    if (!ts || !peek(ts + clk::kTsActor, actor) || !actor || !peek(actor + clk::kActorUser, user) || !user) {
        return ClockWho::Other;
    }
    if (user2 && user == user2) return ClockWho::User2;
    // Before the host is known (the game's own first load) nothing is gated:
    // no guest can be in yet, and the host's load must set the world clock.
    if (!host_key) return ClockWho::Other;
    uint32_t key = 0;
    if (peek(user + clk::kUserKey, key) && key == host_key) return ClockWho::Host;
    return ClockWho::User2;  // fail safe: a user that is not the host's
}

bool clock_user2_travel(const ClockGame& g, uintptr_t ts, uintptr_t field) {
    uintptr_t actor = 0;
    if (!ts || !field || !g.field_time || !peek(ts + clk::kTsActor, actor)) return false;
    alignas(32) uint8_t ft[clk::kFieldTimeSize] = {};
    void* got = g.field_time(reinterpret_cast<void*>(field), ft);
    if (!got) return false;
    // As the setter's own store (0xE52E910..0xE52E914), without what follows it.
    std::memcpy(reinterpret_cast<void*>(ts + clk::kTsFieldTime), got, clk::kFieldTimeSize);
    return true;
}

uint8_t clock_loading_flag(ClockWho who, uint8_t flag) { return who == ClockWho::User2 ? uint8_t{0} : flag; }

bool clock_gate_install() {
    if (g_active.load()) return true;
    g_base = reinterpret_cast<uintptr_t>(GetModuleHandleW(nullptr));
    if (!code_ok(kTravel, kTravelHead, sizeof kTravelHead, "FieldMover::SyncFieldTimeOnMove") ||
        !code_ok(kTravelOwnerCmp, kTravelOwnerCmpBytes, sizeof kTravelOwnerCmpBytes, "its owner check") ||
        !code_ok(kTravelGetterCall, kTravelGetterCallBytes, sizeof kTravelGetterCallBytes, "its getter call") ||
        !code_ok(kTravelCont, kTravelContBytes, sizeof kTravelContBytes, "the caller after the inlined body") ||
        !code_ok(kLoading, kLoadingHead, sizeof kLoadingHead, "TransformSync LoadingComplete") ||
        !code_ok(kLoadingFlagCmp, kLoadingFlagCmpBytes, sizeof kLoadingFlagCmpBytes, "its flag check") ||
        !code_ok(kGetter, kGetterHead, sizeof kGetterHead, "CommonField::GetCurrentFieldTime")) {
        return false;
    }
    g_game.field_time = reinterpret_cast<decltype(g_game.field_time)>(g_base + kGetter);
    g_loading = safetyhook::create_inline(reinterpret_cast<void*>(g_base + kLoading),
                                          reinterpret_cast<void*>(&loading_detour), SafetyHookInline::StartDisabled);
    g_travel = safetyhook::create_mid(reinterpret_cast<void*>(g_base + kTravel), travel_mid);
    if (!g_loading || !g_travel || !g_loading.enable()) {
        g_loading = {};
        g_travel = {};
        log::error("Session2 clock gate: cannot hook the clock functions; the gate stays off");
        return false;
    }
    g_active.store(true);
    CloseHandle(CreateThread(nullptr, 0, watch_thread, nullptr, 0, nullptr));
    log::info("Session2 clock gate ON: user 2's travels and loads never publish the world clock "
              "(hooks exe+0x{:X} SyncFieldTimeOnMove, exe+0x{:X} LoadingComplete); world clock now {}",
              kTravel, kLoading, show(world_now()));
    return true;
}

bool clock_gate_active() { return g_active.load(); }

std::string clock_gate_describe() {
    return std::format("clock gate: user 2 travels {} (copy failed {}), loads {} (flag cut {}), world clock moved "
                       "during them {} | world clock {}, publishes seen {} (frozen {}) | host key 0x{:X}",
                       g_travels.load(), g_travel_failed.load(), g_loadings.load(), g_flags_cut.load(),
                       g_world_moved.load(), show(world_now()), g_world_changes.load(), g_world_frozen.load(),
                       g_host_key.load());
}

}  // namespace cdmp::session2
