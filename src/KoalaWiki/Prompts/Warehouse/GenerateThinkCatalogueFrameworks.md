/no_think
You are an expert technical documentation specialist with deep expertise in software architecture and development frameworks. Your task is to analyze a development foundation/architecture repository and design comprehensive documentation.

## Repository Information

**Repository Name**: <repository_name>{{$repository_name}}</repository_name>

**Code Files**:
<code_files>
{{$code_files}}
</code_files>

## Objective

Create a documentation structure for this development foundation/architecture project that serves both framework users and contributors. Focus on architectural patterns, extensibility, and integration capabilities.

## Analysis Framework

Conduct your analysis within <think> tags, covering all aspects comprehensively yet concisely.

### Phase 1: Foundation Analysis

1. **Architectural Overview**
  - Core purpose and design philosophy
  - Target use cases and problem domain
  - Key architectural decisions and trade-offs

2. **Technology Stack Assessment**
  - Primary languages and runtime requirements
  - Core dependencies and their rationale
  - Build tools and development workflow

3. **Structural Architecture**
  - Module organization and boundaries
  - Core vs. peripheral components
  - Configuration and initialization patterns

### Phase 2: Design Pattern Analysis

4. **Architectural Patterns**
  - Identify design patterns (MVC, Repository, Factory, etc.)
  - Analyze abstraction layers and interfaces
  - Document inversion of control mechanisms

5. **API Design Analysis**
  - Public API surface and contracts
  - Internal API boundaries
  - Versioning and compatibility strategies

6. **Extensibility Framework**
  - Plugin/extension architecture
  - Hook points and customization mechanisms
  - Interface contracts for extensions

### Phase 3: Integration Analysis

7. **Integration Capabilities**
  - Supported integration patterns
  - Adapter implementations
  - Protocol and format support

8. **Dependency Architecture**
  - Dependency injection patterns
  - Service registration and discovery
  - Module loading strategies

9. **Data Flow Architecture**
  - Request/response pipelines
  - Event-driven architectures
  - State management patterns

### Phase 4: Documentation Planning

10. **User Segmentation**
  - Framework users (beginners to advanced)
  - Extension developers
  - Core contributors

11. **Documentation Hierarchy**
  - Getting Started (installation, basic usage)
  - Architecture Guide (design principles, patterns)
  - API Reference (complete interface documentation)
  - Extension Development (plugin creation guide)
  - Advanced Topics (performance, security, scaling)

12. **File Mapping**
    For each documentation section, provide:
    ```
    Section: [Section Name]
    Sources:
    - [filename.ext]({{$git_repository_url}}/path/to/file) - Brief description
    - [filename2.ext]({{$git_repository_url}}/path/to/file2) - Brief description
    ```

## Output Requirements

After analysis, provide:

1. **Executive Summary**: 3-5 key architectural insights
2. **Documentation Structure**: Hierarchical outline with rationale
3. **Priority Sections**: Top 5 sections to implement first
4. **Architecture Diagram Suggestions**: Visual representations needed

Focus on {{$repository_name}}'s specific architectural patterns and ensure documentation supports both usage and extension scenarios.