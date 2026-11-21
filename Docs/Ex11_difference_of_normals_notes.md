# Exercise 11, analysis 2: Difference of Normals on the Nisse scan

Script: `Docs/Ex11_difference_of_normals.py`. Run it with the rtcg3d interpreter (`C:\Users\trist\anaconda3\envs\rtcg3d\python.exe Ex11_difference_of_normals.py`). It uses Open3D for the normals, SciPy cKDTree for the neighbour queries, trimesh for loading and topology, PyVista for off-screen rendering and matplotlib for the histograms. Total runtime on this machine is about 7 seconds. Figures and the console log are in `Captures/Ex11/don/`.

## What Difference of Normals is and why

Difference of Normals (Ioannou et al., 2012) estimates the surface normal at every point twice, once from a small neighbourhood and once from a large one, and takes DoN = (n_small - n_large) / 2. Both normals are unit vectors, so the length of DoN is between 0 and 1 and equals sin(theta / 2), where theta is the angle between the two normals. A value of 0.2 means about 23 degrees between them, 0.5 means 60 degrees, 0.707 means 90 degrees.

The idea is the same as Difference of Gaussians on an image. A small neighbourhood sees fine detail and noise, a large one sees only the overall shape. Where the surface is smooth the two agree and DoN is near 0. Where there is a bump, crease, fold or scan noise, the small-scale normal wobbles while the large-scale normal stays put, and DoN grows. So it works as a band-pass filter on surface orientation: it ignores both the overall shape and anything finer than the small radius, and responds to the scales in between. In the lecture list it appears as "Difference of Normals features".

## How the normals are computed

For each point, Open3D collects every point within the radius using a KDTree radius search (`KDTreeSearchParamRadius`). It builds the 3x3 covariance matrix of those neighbours. The eigenvector with the smallest eigenvalue is the direction in which the neighbours spread least, which is the normal of the best-fit plane. This is PCA of the neighbourhood. The KDTree is what makes a radius query fast; without it every query would compare against all 126,286 points.

PCA gives a normal only up to sign, because if n is an eigenvector then so is -n. Open3D picks a sign that is effectively arbitrary: 39 to 44 percent of the points needed a flip in my run, depending on the radius. This matters a lot for DoN. If the small and large normal at a point have opposite signs, their difference has length close to 2 instead of close to 0, and the operator would be measuring sign flips instead of geometry. I fixed it by flipping every normal, at both scales, so that it points to the same side as the mesh's own vertex normal (dot product at least 0). Both scales are oriented against the same reference, so they agree on sign. This works because the mesh has consistent winding (the Ex11 analysis 1 found winding consistent), so the vertex normals all point outward.

The point cloud is the mesh vertices after welding the UV-seam duplicates: 145,922 vertices become 126,286, the same as in analysis 1. I used the vertices and not uniform surface sampling because they are the measured samples, they keep the mesh connectivity that I need for boundary edges, and the density is the scan's own. Uniform sampling would give even density but would invent points. One practical detail: trimesh refuses to weld vertices that carry different UVs, so the script strips the texture attributes before welding.

All radii are given as a fraction of the bounding-box diagonal (1.0522 in OBJ units), so they do not depend on the scale of the file.

## Radius pairs and the choice

Six pairs were tried. The threshold column counts points with |DoN| above 0.2.

| small % | large % | median neighbours in small radius | mean | median | p95 | above 0.2 |
|---|---|---|---|---|---|---|
| 0.3 | 2.0 | 8 | 0.203 | 0.148 | 0.603 | 37.4% |
| 0.3 | 3.0 | 8 | 0.233 | 0.177 | 0.627 | 44.9% |
| 0.3 | 4.0 | 8 | 0.253 | 0.201 | 0.639 | 50.2% |
| 0.5 | 2.0 | 23 | 0.161 | 0.113 | 0.492 | 27.5% |
| 0.5 | 3.0 | 23 | 0.197 | 0.144 | 0.575 | 36.9% |
| 0.5 | 4.0 | 23 | 0.222 | 0.170 | 0.609 | 43.2% |

Each pair, including the two normal estimations, takes 0.3 to 1.1 seconds. Larger radii cost more because more neighbours are gathered.

The chosen pair is small 0.5 percent (0.0053 units) and large 2.0 percent (0.0210 units). The reasons, from the table:

- The 0.3 percent radius contains only 8 neighbours at the median. A plane fit to 8 noisy points is unstable, so its normal mostly reflects noise and the rough fraction (37 to 50 percent) is inflated. I required at least 15 neighbours.
- Among the remaining pairs I picked the one with the largest ratio of the 95th percentile to the median (4.35 for 0.5/2.0, 3.99 for 0.5/3.0, 3.58 for 0.5/4.0), meaning the distribution has the longest tail relative to its typical value, so the rough class stands out best from the smooth bulk. The rule also required the share above the threshold to stay between 2 and 40 percent.
- The larger the large radius, the more the large normal starts to follow the overall form of the figure, so DoN stops marking detail and starts marking the shape of limbs and the hat. 2 percent is 0.021 units on a figure 0.876 units tall, about the size of a bump in the beard.

This is a judgement backed by the table, not a unique optimum. The ranking rule is my own heuristic, and the 0.5/3.0 pair would tell almost the same story.

## Results for the chosen pair

The overall level is not low. The mean |DoN| is 0.161 and the median 0.113, so even typical surface has about 13 degrees between the two normals. That is the noise floor of this scan and not something to subtract away, but it explains why the threshold has to be well above zero.

Segmentation with |DoN| > 0.2 (a 23 degree difference):
- rough or high-detail: 34,683 points, 27.5 percent
- smooth: 91,603 points, 72.5 percent

Other thresholds: 0.1 gives 54.5 percent, 0.3 gives 14.6 percent, 0.5 gives 4.8 percent. The choice of 0.2 is arbitrary, it sits near the elbow of the histogram. The top of the range is 0.742 at the 99th percentile.

Sign check: 1,694 points (1.34 percent) still have normals more than 90 degrees apart after orientation. These are places where the mesh normal itself is unreliable or the surface really folds, so they are the most uncertain values.

## What the images show

All in `Captures/Ex11/don/`.

- `don_front.png` and `don_back.png`: |DoN| as a colour map on the mesh, dark blue is smooth, red is rough. The hat, torso and back are mostly dark blue with a faint grain. The beard and the tufts of hair on the chest are full of green, yellow and red, as expected for fine, curled geometry. The base plate has a spotty pattern of high values, the sign of a bumpy scanned surface. Sharp creases show as thin bright lines: the hat tip, the folded flap on the back, the belt line, the edges of the legs.
- `don_segmentation_front.png` and `don_segmentation_back.png`: the same data with the threshold applied, red for rough. The red concentrates on the beard, the base plate and a few creases, and the back is almost entirely grey.
- `don_histogram.png`: the distribution of |DoN| for the chosen pair and the 0.5/4.0 pair. Both are skewed with a long right tail. The larger radius pair shifts everything to the right, mean 0.222 against 0.161.
- `don_boundary.png`, `don_boundary_below.png`: the boundary vertices in red. From the front they are hardly visible. From below they show up as a ring and small loops under the base plate.
- `don_boundary_profile.png`: mean |DoN| by distance to the nearest boundary vertex.

## What it says about the scan

Hypothesis 1, that |DoN| peaks at the 325 boundary edges, held, with a limit. The boundary has 325 edges and 325 vertices. Mean |DoN| is 0.504 on those vertices, 0.421 for points within 2 percent of the diagonal of a boundary vertex, and 0.155 further away, a ratio of 2.7. The profile decays smoothly: 0.529 within 0.25 percent, 0.379 at 0.5 to 1 percent, 0.296 at 2 to 4 percent, and the baseline of about 0.153 beyond 4 percent. The elevated band reaching 4 percent matches the large radius of about 2 percent plus the small one, which is what you expect if the effect comes from one-sided neighbourhoods at the edge. The limit is that the region is tiny: only 2.18 percent of the points are within 2 percent of the boundary, and only 6.5 percent of the rough points are there. So the holes are a strong local signal but they explain very little of the roughness overall. The boundary is under the base plate, in the stray geometry at the bottom, not on the visible figure.

Hypothesis 2, that |DoN| marks the noisy, patchy-normal regions, is supported moderately. As an independent measure of patchy normals I took, for each vertex, the mean angle between the normals of neighbouring faces (the dihedral angle). The Spearman correlation with |DoN| is 0.44. Where that dihedral angle is in the top 10 percent (above 21 degrees), the mean |DoN| is 0.247, against 0.151 elsewhere. Only 17.4 percent of the rough points fall in the top 10 percent of dihedral angles, so the two measures overlap but are not the same thing. This is expected, since DoN looks at a larger neighbourhood and the dihedral angle only at the immediate triangles.

Connection to the earlier exercises, stated carefully:
- Ex7 patchy cell shading: the data fits the idea. Cell shading thresholds the dot product of the normal and the light, so a normal that varies on a fine scale produces patches where the threshold is crossed. The regions with high |DoN| and high dihedral angle (beard, base plate) are the same places where the patchy look would be expected. I did not compare the Ex7 screenshots against this map, so this is a hypothesis, not a result.
- Ex9 shoulder stretching: nothing in this data supports a link. The shoulders are mostly dark blue in the |DoN| maps. Stretching at the shoulders is more likely a skinning or weight problem than a geometry noise problem, and DoN says nothing about that.

## Limits and uncertainties

- Sign orientation: the whole method depends on the reference normal. I used the mesh's own vertex normals, which are themselves a fine-scale estimate, so at the 1.3 percent of points mentioned above the sign can be wrong. A tangent-plane propagation would be an alternative.
- Scale choice: the numbers change a lot with the radius pair (rough fraction from 27.5 to 50 percent in the table). Results are relative to the radii and the 0.2 threshold, not absolute properties of the mesh.
- Noise: the median |DoN| is 0.113 even on visually smooth regions, so noise, real detail and creases are not separated. A single |DoN| value does not tell which of them is present.
- Point density: PCA at a fixed radius assumes uneven density does not matter. Photogrammetry meshes vary in triangle size, and the small radius contains 23 points only at the median.
- Edges: near boundaries and thin parts (the hat tip) the large neighbourhood is one-sided or crosses to the other side of a thin sheet, which raises |DoN| without the surface being noisy there.
- Single run on one mesh, no repetition or ground truth for what counts as rough.

## Terms to be able to explain at the oral exam

- Surface normal and how it is estimated from a point cloud; PCA, covariance matrix, eigenvector of the smallest eigenvalue.
- Neighbourhood by radius search and by k nearest neighbours; KDTree and why it is faster than a list.
- Normal sign ambiguity and orientation (consistent winding, orienting to a reference).
- Difference of Normals, |DoN| = sin(theta / 2), and why it acts like a band-pass filter (compare Difference of Gaussians).
- Support radius or scale, and why it is expressed relative to the bounding-box diagonal.
- Thresholding for segmentation, and why the threshold is a choice.
- Boundary edge, hole, watertight, and the effect of a mesh boundary on neighbourhood-based operators.
- Dihedral angle between adjacent faces.
- Welding vertices and UV-seam duplicates.
- Spearman rank correlation.

Built with Claude Code
