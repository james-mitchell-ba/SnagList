import cadquery as cq

DENSITY_G_CM3 = 8.0  # 316L stainless
FABRIC = 1.0
HOLE_L, HOLE_W = 70.0, 25.0
MARGIN = 8.0
A_T, B_T = 2.0, 1.5
GASKET_T = 1.0
WALL = 1.2
CLEAR = 0.2
TUBE_H = FABRIC + GASKET_T + B_T - 0.2
SCREW_D = 3.2
SCREWS = [(40.5, 0), (-40.5, 0), (20, 18), (-20, 18), (20, -18), (-20, -18)]


def stadium(l, w, z0, h):
    return cq.Workplane("XY").workplane(offset=z0).slot2D(l, w).extrude(h)


outer_l, outer_w = HOLE_L + 2 * MARGIN, HOLE_W + 2 * MARGIN
tube_l, tube_w = HOLE_L - 2 * CLEAR, HOLE_W - 2 * CLEAR

a = stadium(outer_l, outer_w, 0, A_T)
a = a.edges("not |Z").fillet(0.7)
tube = stadium(tube_l, tube_w, A_T, TUBE_H).cut(stadium(tube_l - 2 * WALL, tube_w - 2 * WALL, A_T, TUBE_H))
a = a.union(tube)
a = a.cut(stadium(tube_l - 2 * WALL, tube_w - 2 * WALL, -1, A_T + TUBE_H + 2))
a = a.faces("<Z").edges(cq.selectors.BoxSelector((-34, -11, -0.1), (34, 11, 0.1))).fillet(1.2)
a = a.faces(">Z").edges(cq.selectors.BoxSelector((-34, -11.5, A_T + TUBE_H - 0.1), (34, 11.5, A_T + TUBE_H + 0.1))).fillet(0.5)
a = a.cut(cq.Workplane("XY").pushPoints(SCREWS).circle(SCREW_D / 2).extrude(A_T + 1).translate((0, 0, -0.5)))

b = stadium(outer_l, outer_w, 0, B_T).edges("not |Z").fillet(0.6)
b = b.cut(stadium(HOLE_L + 0.2, HOLE_W + 0.2, -1, B_T + 2))
b = b.cut(cq.Workplane("XY").pushPoints(SCREWS).circle(SCREW_D / 2).extrude(B_T + 1).translate((0, 0, -0.5)))
b = b.translate((0, 0, A_T + FABRIC + GASKET_T))

g = stadium(outer_l, outer_w, 0, GASKET_T).edges("not |Z").fillet(0.6)
g = g.cut(stadium(tube_l + 0.1, tube_w + 0.1, -1, GASKET_T + 2))
g = g.cut(cq.Workplane("XY").pushPoints(SCREWS).circle(SCREW_D / 2).extrude(GASKET_T + 1).translate((0, 0, -0.5)))
g_pos = g.translate((0, 0, A_T + FABRIC))

asm = cq.Assembly().add(a, name="rear_plate_A", color=cq.Color(0.15, 0.15, 0.15)).add(g_pos, name="gasket", color=cq.Color(0.05, 0.05, 0.05)).add(b, name="front_ring_B", color=cq.Color(0.15, 0.15, 0.15))
here = __file__.rsplit("/", 1)[0]
asm.save(f"{here}/oval_grommet_assembly.step")
cq.exporters.export(a, f"{here}/oval_grommet_A_rear.step")
cq.exporters.export(b.translate((0, 0, -(A_T + FABRIC + GASKET_T))), f"{here}/oval_grommet_B_front.step")
cq.exporters.export(g, f"{here}/oval_grommet_gasket.step")
for n, s in (("A", a), ("B", b)):
    bb = s.val().BoundingBox()
    print(n, round(bb.xlen, 2), round(bb.ylen, 2), round(bb.zlen, 2), round(s.val().Volume() * DENSITY_G_CM3 * 1e-3, 1), "g", s.val().isValid())
